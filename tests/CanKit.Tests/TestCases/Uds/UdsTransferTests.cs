using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CanKit.Abstractions.API.Can;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Abstractions.API.Common.Definitions;
using CanKit.Core;
using CanKit.Pro.IsoTp;
using CanKit.Pro.Uds;
using FluentAssertions;
using Xunit;
using IsoTpFactory = CanKit.Pro.IsoTp.IsoTp;

namespace CanKit.Tests.TestCases.Uds;

/// <summary>
/// Integration tests covering FR-UDS-012 (RequestDownload / RequestUpload / TransferData /
/// RequestTransferExit — services 0x34/0x35/0x36/0x37) against a <see cref="SimulatedUdsEcu"/>
/// wired to a real ISO-TP stack over the Virtual bus. Mirrors the setup used by
/// <see cref="UdsClientTests"/>.
/// </summary>
public class UdsTransferTests : IClassFixture<TestCaseProvider>
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(10);

    private static string NewSession() => $"uds-transfer-{Guid.NewGuid():N}";

    private static ICanBus OpenClassic(string session, int channel) => CanBus.Open(
        $"virtual://{session}/{channel}",
        cfg => cfg.SetProtocolMode(CanProtocolMode.Can20).Baud(TestCaseProvider.AbitRate));

    private static IsoTpChannelOptions FastIsoTp() => new()
    {
        UseCanFd = false,
        UsePadding = true,
        NAs = TimeSpan.FromMilliseconds(500),
        NBs = TimeSpan.FromMilliseconds(500),
        NCr = TimeSpan.FromMilliseconds(500),
    };

    private static (IUdsClient client, SimulatedUdsEcu ecu, IDisposable dispose) BuildPair(
        Action<SimulatedUdsEcu> configure, UdsClientOptions? options = null)
    {
        var session = NewSession();
        var busClient = OpenClassic(session, 0);
        var busEcu = OpenClassic(session, 1);

        var clientEndpoint = IsoTpEndpoint.Normal(txCanId: 0x7E0, rxCanId: 0x7E8);
        var ecuEndpoint = IsoTpEndpoint.Normal(txCanId: 0x7E8, rxCanId: 0x7E0);

        var clientChannel = IsoTpFactory.Open(busClient, clientEndpoint, FastIsoTp());
        var ecuChannel = IsoTpFactory.Open(busEcu, ecuEndpoint, FastIsoTp());

        var ecu = new SimulatedUdsEcu(ecuChannel);
        configure(ecu);
        ecu.Start();

        var client = UdsClient.Create(clientChannel, options);
        var dispose = new CompositeDisposable(
            client, ecu, ecuChannel, clientChannel, busEcu, busClient);
        return (client, ecu, dispose);
    }

    /// <summary>
    /// Configures <paramref name="ecu"/> as a minimal download server:
    ///   0x34 accepts the given format/address/size and replies with a fixed max block length,
    ///   0x36 walks the block-sequence counter starting at 0x01 (wrapping 0xFF→0x00→0x01…) and
    ///        rejects any mismatch with NRC 0x73 (wrongBlockSequenceCounter),
    ///   0x37 accepts unconditionally and echoes an empty transferResponseParameterRecord.
    /// The <see cref="List{Byte}"/> capture holds the concatenated payload in order.
    /// </summary>
    private static (List<byte> capture, List<byte> seenBsc) WireDownloadEcu(
        SimulatedUdsEcu ecu, ulong maxBlockLength, int maxBlockWidth = 2)
    {
        var capture = new List<byte>();
        var seenBsc = new List<byte>();
        byte expectedBsc = 0x01;

        ecu.On(0x34, req =>
        {
            // Positive-response body layout: [lengthFormatIdentifier][maxNumberOfBlockLength…].
            byte lfid = (byte)((maxBlockWidth & 0x0F) << 4);
            var body = new byte[1 + maxBlockWidth];
            body[0] = lfid;
            for (int i = 0; i < maxBlockWidth; i++)
                body[1 + i] = (byte)(maxBlockLength >> (8 * (maxBlockWidth - 1 - i)));
            return body;
        });

        ecu.On(0x36, req =>
        {
            byte bsc = req[1];
            if (bsc != expectedBsc)
                throw new EcuNegativeResponse(0x73); // wrongBlockSequenceCounter

            seenBsc.Add(bsc);
            for (int i = 2; i < req.Length; i++) capture.Add(req[i]);
            unchecked { expectedBsc++; }
            // Positive response body: echoed BSC + empty transferResponseParameterRecord.
            return new byte[] { bsc };
        });

        ecu.On(0x37, req => Array.Empty<byte>());

        return (capture, seenBsc);
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-012 — full 0x34 → 0x36 → 0x37 cycle with a caller-driven BSC.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task Download_Full_Cycle_Round_Trips_Payload()
    {
        var (client, ecu, dispose) = BuildPair(e => { });
        using (dispose)
        {
            // Sniff every RequestDownload frame so we can assert the exact byte layout
            // (dataFormatIdentifier + addressAndLengthFormatIdentifier + memoryAddress +
            // memorySize) reaches the wire in ISO 14229-1 §14.2.2 order.
            byte[]? downloadRequest = null;
            ecu.On(0x34, req =>
            {
                downloadRequest = req.ToArray();
                // maxNumberOfBlockLength = 6 (1 byte width). Chunk size = 6 - 2 = 4 payload bytes,
                // so an 8-byte payload will need exactly 2 TransferData rounds.
                return new byte[] { 0x10, 0x06 };
            });
            var (capture, seenBsc) = (new List<byte>(), new List<byte>());
            byte expectedBsc = 0x01;
            ecu.On(0x36, req =>
            {
                req[1].Should().Be(expectedBsc);
                seenBsc.Add(req[1]);
                for (int i = 2; i < req.Length; i++) capture.Add(req[i]);
                unchecked { expectedBsc++; }
                return new byte[] { req[1] };
            });
            byte[]? exitRequest = null;
            ecu.On(0x37, req => { exitRequest = req.ToArray(); return new byte[] { 0xDE, 0xAD }; });

            var addr = new byte[] { 0x00, 0x10, 0x00, 0x00 };
            var size = new byte[] { 0x00, 0x00, 0x00, 0x08 };
            var payload = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88 };

            var setup = await client.RequestDownloadAsync(
                dataFormatIdentifier: 0x00,
                addressAndLengthFormatIdentifier: 0x44,
                memoryAddress: addr,
                memorySize: size,
                cancellationToken: new CancellationTokenSource(ShortTimeout).Token);

            setup.LengthFormatIdentifier.Should().Be(0x10);
            setup.MaxNumberOfBlockLength.Should().Be(6UL);

            // Wire layout check: [0x34][DFI][ALFI][addr…][size…].
            downloadRequest.Should().NotBeNull();
            downloadRequest!.Should().Equal(new byte[]
            {
                0x34, 0x00, 0x44, 0x00, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08,
            });

            byte bsc = 0x01;
            var record1 = await client.TransferDataAsync(bsc, payload.AsMemory(0, 4),
                new CancellationTokenSource(ShortTimeout).Token);
            record1.Should().BeEmpty();

            bsc = 0x02;
            var record2 = await client.TransferDataAsync(bsc, payload.AsMemory(4, 4),
                new CancellationTokenSource(ShortTimeout).Token);
            record2.Should().BeEmpty();

            await client.RequestTransferExitAsync(default,
                new CancellationTokenSource(ShortTimeout).Token);

            capture.Should().Equal(payload);
            seenBsc.Should().Equal(new byte[] { 0x01, 0x02 });
            exitRequest.Should().NotBeNull();
            exitRequest!.Should().Equal(new byte[] { 0x37 });
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-012 — bad BSC yields NRC 0x73 (wrongBlockSequenceCounter) as a structured NRC.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task TransferData_Wrong_BlockSequenceCounter_Yields_Nrc_0x73()
    {
        var (client, ecu, dispose) = BuildPair(e =>
        {
            e.On(0x34, _ => new byte[] { 0x10, 0x10 });
            byte expected = 0x01;
            e.On(0x36, req =>
            {
                if (req[1] != expected)
                    throw new EcuNegativeResponse(0x73); // wrongBlockSequenceCounter
                unchecked { expected++; }
                return new byte[] { req[1] };
            });
        });
        using (dispose)
        {
            await client.RequestDownloadAsync(
                dataFormatIdentifier: 0x00,
                addressAndLengthFormatIdentifier: 0x11,
                memoryAddress: new byte[] { 0x10 },
                memorySize: new byte[] { 0x08 },
                cancellationToken: new CancellationTokenSource(ShortTimeout).Token);

            // Skip BSC=0x01 and send BSC=0x05 instead — ECU rejects.
            Func<Task> bad = () => client.TransferDataAsync(0x05,
                new byte[] { 0xAA, 0xBB },
                new CancellationTokenSource(ShortTimeout).Token);
            var ex = (await bad.Should().ThrowAsync<UdsNegativeResponseException>()).Which;
            ex.RequestedService.Should().Be(UdsServiceId.TransferData);
            ex.Code.Should().Be(0x73);
            ex.CodeAsEnum.Should().Be(UdsNegativeResponseCode.WrongBlockSequenceCounter);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-012 — convenience DownloadAsync chunks payload according to max block length,
    // walks BSC 0x01.., wraps 0xFF→0x00→0x01, and issues RequestTransferExit at the end.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task DownloadAsync_Multi_Block_Uses_BSC_Wrap()
    {
        // maxNumberOfBlockLength = 4 → chunk size = 4 - 2 = 2 payload bytes/block.
        // A 258×2 = 516-byte payload needs 258 TransferData calls, which forces the BSC to
        // roll 0x01..0xFF, wrap to 0x00, and continue to 0x01 → 0x02 (258 blocks total).
        const int blockCount = 258;
        const int chunkSize = 2;
        var payload = new byte[blockCount * chunkSize];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte)((i * 7) & 0xFF);

        var (client, ecu, dispose) = BuildPair(e => { });
        using (dispose)
        {
            var (capture, seenBsc) = WireDownloadEcu(ecu, maxBlockLength: 4);
            bool exitSeen = false;
            ecu.On(0x37, _ => { exitSeen = true; return Array.Empty<byte>(); });

            await client.DownloadAsync(
                dataFormatIdentifier: 0x00,
                addressAndLengthFormatIdentifier: 0x11,
                memoryAddress: new byte[] { 0x10 },
                memorySize: new byte[] { (byte)payload.Length }, // sizeWidth=1 caps at 255, but
                // the ECU only echoes width from ALFI; we simply need a well-formed 1-byte size
                // for the wire encoding test (this test does not verify size semantics).
                data: payload,
                cancellationToken: new CancellationTokenSource(ShortTimeout).Token);

            capture.Should().Equal(payload);
            seenBsc.Should().HaveCount(blockCount);

            // First BSC is 0x01 and the sequence wraps 0xFF → 0x00 → 0x01 → 0x02.
            seenBsc[0].Should().Be(0x01);
            seenBsc[0xFE].Should().Be(0xFF); // 255th block (index 254 → BSC=0xFF).
            seenBsc[0xFF].Should().Be(0x00); // 256th block wraps to 0x00.
            seenBsc[0x100].Should().Be(0x01);
            seenBsc[0x101].Should().Be(0x02);

            exitSeen.Should().BeTrue();
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-012 — RequestUpload mirrors RequestDownload; parses maxNumberOfBlockLength across
    // a multi-byte width (2 bytes here, LFID high nibble = 2).
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task RequestUpload_Parses_MaxNumberOfBlockLength_Multi_Byte()
    {
        var (client, ecu, dispose) = BuildPair(e => e.On(0x35, req =>
        {
            req[0].Should().Be(0x35);
            req[1].Should().Be(0x00); // dataFormatIdentifier
            req[2].Should().Be(0x11); // addressAndLengthFormatIdentifier: addr=1, size=1
            req[3].Should().Be(0x10); // memoryAddress
            req[4].Should().Be(0x40); // memorySize
            // 2-byte maxNumberOfBlockLength = 0x0400.
            return new byte[] { 0x20, 0x04, 0x00 };
        }));
        using (dispose)
        {
            var setup = await client.RequestUploadAsync(
                dataFormatIdentifier: 0x00,
                addressAndLengthFormatIdentifier: 0x11,
                memoryAddress: new byte[] { 0x10 },
                memorySize: new byte[] { 0x40 },
                cancellationToken: new CancellationTokenSource(ShortTimeout).Token);

            setup.LengthFormatIdentifier.Should().Be(0x20);
            setup.MaxNumberOfBlockLength.Should().Be(0x0400UL);
        }
    }

    // -----------------------------------------------------------------------------------
    // FR-UDS-012 — RequestTransferExit carries the caller's transferRequestParameterRecord
    // (e.g. CRC) on the wire; the client succeeds as long as the ECU responds positively.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task RequestTransferExit_Sends_Optional_Parameter_Record()
    {
        byte[]? seenRequest = null;
        var (client, _, dispose) = BuildPair(e => e.On(0x37, req =>
        {
            seenRequest = req.ToArray();
            return new byte[] { 0xAB, 0xCD };
        }));
        using (dispose)
        {
            await client.RequestTransferExitAsync(new byte[] { 0x12, 0x34, 0x56 },
                new CancellationTokenSource(ShortTimeout).Token);
            seenRequest.Should().NotBeNull();
            seenRequest!.Should().Equal(new byte[] { 0x37, 0x12, 0x34, 0x56 });
        }
    }

    // -----------------------------------------------------------------------------------
    // DownloadAsync must reject an ECU that advertises a maxNumberOfBlockLength ≤ 2 (would
    // leave zero payload bytes per TransferData). We surface that as a protocol violation
    // rather than looping forever with zero-byte chunks.
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task DownloadAsync_Refuses_Impossible_Max_Block_Length()
    {
        var (client, _, dispose) = BuildPair(e => e.On(0x34, _ => new byte[] { 0x10, 0x02 }));
        using (dispose)
        {
            Func<Task> act = () => client.DownloadAsync(
                dataFormatIdentifier: 0x00,
                addressAndLengthFormatIdentifier: 0x11,
                memoryAddress: new byte[] { 0x10 },
                memorySize: new byte[] { 0x04 },
                data: new byte[] { 1, 2, 3, 4 },
                cancellationToken: new CancellationTokenSource(ShortTimeout).Token);
            await act.Should().ThrowAsync<UdsProtocolException>()
                .WithMessage("*maxNumberOfBlockLength*");
        }
    }

    // -----------------------------------------------------------------------------------
    // The client must reject a mismatched addressAndLengthFormatIdentifier before touching
    // the wire (defensive contract check, so callers see the error at their call site).
    // -----------------------------------------------------------------------------------
    [Fact]
    public async Task RequestDownload_Rejects_Buffer_Width_Mismatch()
    {
        var (client, _, dispose) = BuildPair(e => { });
        using (dispose)
        {
            // ALFI 0x44 says both addr and size are 4 bytes; passing a 2-byte addr is wrong.
            Func<Task> act = () => client.RequestDownloadAsync(
                dataFormatIdentifier: 0x00,
                addressAndLengthFormatIdentifier: 0x44,
                memoryAddress: new byte[] { 0x10, 0x00 },
                memorySize: new byte[] { 0x00, 0x00, 0x00, 0x08 },
                cancellationToken: new CancellationTokenSource(ShortTimeout).Token);
            await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        }
    }

    private sealed class CompositeDisposable : IDisposable
    {
        private readonly IDisposable[] _items;
        public CompositeDisposable(params IDisposable[] items) => _items = items;
        public void Dispose()
        {
            foreach (var d in _items)
            {
                try { d.Dispose(); } catch { /* ignored */ }
            }
        }
    }
}

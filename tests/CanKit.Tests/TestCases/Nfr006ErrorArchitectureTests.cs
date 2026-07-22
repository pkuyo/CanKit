using System;
using CanKit.Core.Exceptions;
using CanKit.Pro.CANopen;
using CanKit.Pro.CANopen.Sdo;
using CanKit.Pro.IsoTp;
using CanKit.Pro.J1939;
using CanKit.Pro.J1939Tp;
using CanKit.Pro.Uds;
using FluentAssertions;
using Xunit;

namespace CanKit.Tests.TestCases;

/// <summary>
/// NFR-006 / arc42 ADR-12: every L3/L4 protocol exception derives from <see cref="CanKitException"/>
/// and maps to its documented 6xxx error code, so library-wide failures can be caught and
/// classified uniformly instead of through six package-local hierarchies.
/// </summary>
public class Nfr006ErrorArchitectureTests
{
    [Fact]
    public void All_Protocol_Exceptions_Derive_From_CanKitException()
    {
        var exceptionTypes = new[]
        {
            typeof(IsoTpException), typeof(IsoTpTimeoutException), typeof(IsoTpOverflowException),
            typeof(IsoTpWaitFrameLimitExceededException), typeof(IsoTpSendRejectedException),
            typeof(J1939TpException), typeof(J1939TpAbortException), typeof(J1939TpSendRejectedException),
            typeof(UdsException), typeof(UdsNegativeResponseException), typeof(UdsTimeoutException),
            typeof(UdsProtocolException),
            typeof(J1939NodeException), typeof(J1939NoAddressException), typeof(J1939CannotClaimException),
            typeof(SdoAbortException), typeof(CanOpenTransportException),
        };

        foreach (var t in exceptionTypes)
        {
            typeof(CanKitException).IsAssignableFrom(t).Should().BeTrue(
                $"{t.Name} must derive from CanKitException (ADR-12 / NFR-006)");
        }
    }

    [Fact]
    public void Protocol_Exceptions_Map_To_The_Documented_ErrorCodes()
    {
        ((CanKitException)new IsoTpTimeoutException(IsoTpTimer.NBs, "x"))
            .ErrorCode.Should().Be(CanKitErrorCode.ProtocolTimeout);
        ((CanKitException)new IsoTpOverflowException("x"))
            .ErrorCode.Should().Be(CanKitErrorCode.ProtocolPeerAbort);
        ((CanKitException)new IsoTpWaitFrameLimitExceededException(received: 3, limit: 2))
            .ErrorCode.Should().Be(CanKitErrorCode.ProtocolPeerAbort);
        ((CanKitException)new IsoTpSendRejectedException("x"))
            .ErrorCode.Should().Be(CanKitErrorCode.TransportOperationFailed);

        ((CanKitException)new J1939TpAbortException(J1939TpAbortReason.Timeout, 0xEE00, "x"))
            .ErrorCode.Should().Be(CanKitErrorCode.ProtocolTimeout);
        ((CanKitException)new J1939TpAbortException(J1939TpAbortReason.UnexpectedCtsNumPackets, 0xEE00, "x"))
            .ErrorCode.Should().Be(CanKitErrorCode.ProtocolPeerAbort);
        ((CanKitException)new J1939TpSendRejectedException("x"))
            .ErrorCode.Should().Be(CanKitErrorCode.TransportOperationFailed);

        ((CanKitException)new UdsTimeoutException(UdsServiceId.ReadDataByIdentifier,
                UdsTimeoutTimer.P2Star, TimeSpan.FromMilliseconds(1)))
            .ErrorCode.Should().Be(CanKitErrorCode.ProtocolTimeout);
        ((CanKitException)new UdsNegativeResponseException(UdsServiceId.ReadDataByIdentifier, 0x31))
            .ErrorCode.Should().Be(CanKitErrorCode.ProtocolNegativeResponse);
        ((CanKitException)new UdsProtocolException("x"))
            .ErrorCode.Should().Be(CanKitErrorCode.TransportOperationFailed);

        ((CanKitException)new J1939NoAddressException())
            .ErrorCode.Should().Be(CanKitErrorCode.AddressClaimFailed);
        ((CanKitException)new J1939CannotClaimException(0x42))
            .ErrorCode.Should().Be(CanKitErrorCode.AddressClaimFailed);

        ((CanKitException)new SdoAbortException(0x1000, 0x01, SdoAbortCode.General))
            .ErrorCode.Should().Be(CanKitErrorCode.ProtocolPeerAbort);
        ((CanKitException)new CanOpenTransportException("x"))
            .ErrorCode.Should().Be(CanKitErrorCode.TransportOperationFailed);
    }
}

using System;
using System.Linq;
using Pkuyo.CanKit.Net.Core.Definitions;
using Pkuyo.CanKit.Net.Core.Exceptions;
using Pkuyo.CanKit.Net.Tests.Fakes;

namespace Pkuyo.CanKit.Net.Tests;

public class FramesAndFiltersTests
{
    public enum FilterType
    {
        Range,
        Mask
    }

    public record FilterTestData(uint Num1, uint Num2, CanFilterIDType IdType, FilterType Type);

    public static IEnumerable<object[]> FilterRuleData => new List<object[]>
    {
        new object[]
        {
            new [] 
            {
                new FilterTestData(0x100, 0x200, CanFilterIDType.Standard, FilterType.Mask),
                new FilterTestData(0x55AA, 0xAA55, CanFilterIDType.Standard, FilterType.Mask),
                new FilterTestData(0xCC33, 0x33CC, CanFilterIDType.Extend, FilterType.Mask)
            }
        },
        new object[]
        {
            new [] 
            {
                new FilterTestData(0x100, 0x200, CanFilterIDType.Standard, FilterType.Range),
                new FilterTestData(0x1000, 0x2000, CanFilterIDType.Standard, FilterType.Range),
                new FilterTestData(0x10000, 0x20000, CanFilterIDType.Standard, FilterType.Range)
            }
        },
        new object[]
        {
            new [] 
            {
                new FilterTestData(0x100, 0x200, CanFilterIDType.Standard, FilterType.Range),
                new FilterTestData(0x100, 0x200, CanFilterIDType.Standard, FilterType.Mask),
                new FilterTestData(0x1000, 0x2000, CanFilterIDType.Standard, FilterType.Range),
                new FilterTestData(0xCC33, 0x33CC, CanFilterIDType.Extend, FilterType.Mask),
                new FilterTestData(0x10000, 0x20000, CanFilterIDType.Standard, FilterType.Range)
            }
        },
        new object[]
        {
            new [] 
            {
                new FilterTestData(uint.MinValue, uint.MaxValue, CanFilterIDType.Standard, FilterType.Range),
                new FilterTestData(uint.MinValue, uint.MaxValue, CanFilterIDType.Standard, FilterType.Mask),
                new FilterTestData(uint.MaxValue, uint.MinValue, CanFilterIDType.Standard, FilterType.Mask),
            }
        },

    };
    
    [Fact]
    public void ClassicFrame_DataLength_UpTo8()
    {
        var ok = new CanClassicFrame(rawIDInit: 0x100, dataInit: new ReadOnlyMemory<byte>(new byte[8]));
        Assert.Equal(8, ok.Dlc);
        // Use init setter to trigger validation
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var _ = new CanClassicFrame(rawIDInit: 0x100) { Data = new byte[9] };
        });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 8)]
    [InlineData(12, 9)]
    [InlineData(16, 10)]
    [InlineData(20, 11)]
    [InlineData(24, 12)]
    [InlineData(32, 13)]
    [InlineData(48, 14)]
    [InlineData(64, 15)]
    public void Fd_Dlc_Len_Mapping(int len, byte dlc)
    {
        Assert.Equal(dlc, CanFdFrame.LenToDlc(len));
        Assert.Equal(len, CanFdFrame.DlcToLen(dlc));
    }
    

    [Theory]
    [MemberData(nameof(FilterRuleData))]
    public void Filter_Rules_Are_Recorded(FilterTestData[] data)
    {
        
        var provider = new TestModelProvider();
        var (opt, cfg) = provider.GetChannelOptions(0);
        Assert.Null(opt.Filter);
        
        foreach (var t in data)
        {
            switch (t.Type)
            {
                case FilterType.Mask:
                    cfg.AccMask(t.Num1, t.Num2, t.IdType);
                    break;
                case FilterType.Range:
                    cfg.RangeFilter(t.Num1, t.Num2, t.IdType);
                    break;
            }
         
        }
        Assert.NotNull(opt.Filter);
        Assert.Same(opt.Filter, cfg.Filter); 
        Assert.Equal(data.Length, opt.Filter.FilterRules.Count);

        Assert.Collection(cfg.Filter.FilterRules, 
            data.Select<FilterTestData, Action<FilterRule>>(d => r => 
            {
                if (d.Type == FilterType.Mask)
                {
                    var m = Assert.IsType<FilterRule.Mask>(r);
                    Assert.Equal(d.Num1, m.AccCode);
                    Assert.Equal(d.Num2, m.AccMask);
                    Assert.Equal(d.IdType, m.FilterIdType);
                }
                else
                {
                    var rg = Assert.IsType<FilterRule.Range>(r);
                    Assert.Equal(d.Num1, rg.From);
                    Assert.Equal(d.Num2, rg.To);
                    Assert.Equal(d.IdType, rg.FilterIdType);
                }
            }).ToArray()
        );
    }
    
    [Fact]
    public void RangeFilter_FromGreaterThanTo_Throws()
    {
        var (opt, cfg) = new TestModelProvider().GetChannelOptions(0);
        Assert.Throws<ArgumentException>(() => cfg.RangeFilter(10, 1, CanFilterIDType.Standard));
    }
    
    [Fact]
    public void ClassicFrame_Id_And_Flags_Manipulate_RawID_Correctly()
    {
        var f = new CanClassicFrame(rawIDInit: 0)
        {
            ID = 0xFFFFFFFF, // will be masked to 29 bits
            IsExtendedFrame = true,
            IsRemoteFrame = true,
            IsErrorFrame = true,
            Data = new byte[] { 1, 2, 3 }
        };

        Assert.Equal(0x1FFFFFFFu, f.ID);
        Assert.True(f.IsExtendedFrame);
        Assert.True(f.IsRemoteFrame);
        Assert.True(f.IsErrorFrame);
        Assert.Equal((byte)3, f.Dlc);
    }

    [Fact]
    public void ClassicFrame_Implicit_To_TransmitData_Preserves_Value()
    {
        var f = new CanClassicFrame(0x123, new byte[] { 1, 2 });
        CanTransmitData tx = f;
        Assert.IsType<CanClassicFrame>(tx.CanFrame);
        var back = (CanClassicFrame)tx.CanFrame;
        Assert.Equal(f, back);
    }

    [Fact]
    public void Fd_DlcToLen_Invalid_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanFdFrame.DlcToLen(16));
    }

    [Fact]
    public void Fd_Init_And_Flags_Preserved()
    {
        var fd = new CanFdFrame(rawIdInit: 0)
        {
            ID = 0x456u,
            IsExtendedFrame = true,
            BitRateSwitch = true,
            ErrorStateIndicator = true,
            Data = new byte[12]
        };

        Assert.Equal(0x456u, fd.ID);
        Assert.True(fd.IsExtendedFrame);
        Assert.True(fd.BitRateSwitch);
        Assert.True(fd.ErrorStateIndicator);
        Assert.Equal(9, fd.Dlc); // 12 bytes => dlc 9
    }

    [Fact]
    public void DefaultCanErrorInfo_Roundtrip()
    {
        var frame = new CanClassicFrame(rawIDInit: 0x100);
        var now = DateTime.UtcNow;
        var err = new DefaultCanErrorInfo(FrameErrorKind.BitError, now, 0xDEADu, 123ul, FrameDirection.Rx, frame);

        Assert.Equal(FrameErrorKind.BitError, err.Kind);
        Assert.Equal(now, err.SystemTimestamp);
        Assert.Equal(0xDEADu, err.RawErrorCode);
        Assert.Equal((ulong)123, err.TimeOffset);
        Assert.Equal(FrameDirection.Rx, err.Direction);
        Assert.IsType<CanClassicFrame>(err.Frame);
    }
    
}

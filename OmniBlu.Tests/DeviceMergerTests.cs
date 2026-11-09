using OmniBlu.Managers;
using OmniBlu.Models;

namespace OmniBlu.Tests;

public class DeviceMergerTests
{
    // IDs taken from a real Windows machine: the same phone has a different ID in each list.
    private const string PairedId = "Bluetooth#Bluetooth14:7d:da:0b:09:31-b0:8c:75:1d:06:52";
    private const string EndpointId = @"\\?\BTHENUM#{0000110a-0000-1000-8000-00805f9b34fb}_VID&0001004c_PID&7411#7&200747e8&0&B08C751D0652_C00000000#{6994ad04-93ef-11d0-a3cc-00a0c9223196}\SNK";
    private const string Container = "77f7a4f2-0852-5ba3-baaa-bc2e8ac9ace7";

    [Fact]
    public void Joins_paired_device_and_endpoint_by_container_id()
    {
        var result = DeviceMerger.Merge(
            new[] { new PairedRecord(PairedId, "Henry", Container) },
            new[] { new EndpointRecord(EndpointId, "Henry", Container) });

        var device = Assert.Single(result);
        Assert.Equal(Container, device.Key);
        Assert.Equal("Henry", device.Name);
        Assert.Equal(EndpointId, device.A2dpEndpointId);
        Assert.True(device.SupportsA2dp);
    }

    [Fact]
    public void Keeps_the_normalized_address_of_the_paired_device()
    {
        var result = DeviceMerger.Merge(
            new[] { new PairedRecord(PairedId, "Henry", Container, "B0:8C:75:1D:06:52", 2) },
            Array.Empty<EndpointRecord>());

        Assert.Equal("b08c751d0652", Assert.Single(result).Address);
    }

    [Fact]
    public void Keeps_the_device_class_of_the_paired_device()
    {
        var result = DeviceMerger.Merge(
            new[] { new PairedRecord(PairedId, "Henry", Container, null, 2) },
            Array.Empty<EndpointRecord>());

        Assert.Equal(2, Assert.Single(result).MajorClass);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(31, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(7, false)]
    public void Only_phones_and_computers_are_host_like(int? majorClass, bool expected)
    {
        Assert.Equal(expected, DeviceClass.IsHostLike(majorClass));
    }

    [Fact]
    public void A_device_with_audio_is_host_like_whatever_its_class()
    {
        Assert.True(new SmartDeviceInfo("k", "Odd phone", "endpoint", null, 5).IsHostLike);
        Assert.False(new SmartDeviceInfo("k", "Game controller", null, null, 5).IsHostLike);
    }

    [Fact]
    public void Container_ids_are_compared_ignoring_case()
    {
        var result = DeviceMerger.Merge(
            new[] { new PairedRecord(PairedId, "Henry", Container.ToUpperInvariant()) },
            new[] { new EndpointRecord(EndpointId, "Henry", Container) });

        Assert.Single(result);
        Assert.True(result[0].SupportsA2dp);
    }

    [Fact]
    public void Paired_device_without_endpoint_does_not_support_a2dp()
    {
        var result = DeviceMerger.Merge(
            new[] { new PairedRecord("Bluetooth#x-dc:af:68:09:6c:f8", "Wireless Controller", "3cc8f77b-a0b5-51bf-8ef5-c0989fe8c6b2") },
            Array.Empty<EndpointRecord>());

        var device = Assert.Single(result);
        Assert.False(device.SupportsA2dp);
        Assert.Null(device.A2dpEndpointId);
    }

    [Fact]
    public void Endpoint_without_paired_device_still_gets_an_entry()
    {
        var result = DeviceMerger.Merge(
            Array.Empty<PairedRecord>(),
            new[] { new EndpointRecord(EndpointId, "Henry", Container) });

        var device = Assert.Single(result);
        Assert.Equal(Container, device.Key);
        Assert.True(device.SupportsA2dp);
    }

    [Fact]
    public void Paired_device_without_container_id_falls_back_to_its_own_id()
    {
        var result = DeviceMerger.Merge(
            new[] { new PairedRecord(PairedId, "Henry", "") },
            Array.Empty<EndpointRecord>());

        Assert.Equal(PairedId, Assert.Single(result).Key);
    }

    [Fact]
    public void Missing_names_fall_back_to_the_endpoint_name_then_a_default()
    {
        var fromEndpoint = DeviceMerger.Merge(
            new[] { new PairedRecord(PairedId, "", Container) },
            new[] { new EndpointRecord(EndpointId, "Henry", Container) });
        var fromDefault = DeviceMerger.Merge(
            new[] { new PairedRecord(PairedId, " ", Container) },
            Array.Empty<EndpointRecord>());

        Assert.Equal("Henry", fromEndpoint[0].Name);
        Assert.Equal("Unknown device", fromDefault[0].Name);
    }

    [Fact]
    public void Same_container_is_listed_once()
    {
        var result = DeviceMerger.Merge(
            new[] { new PairedRecord("a", "Henry", Container), new PairedRecord("b", "Henry", Container) },
            Array.Empty<EndpointRecord>());

        Assert.Single(result);
    }
}

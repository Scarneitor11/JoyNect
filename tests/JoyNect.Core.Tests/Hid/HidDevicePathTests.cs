using JoyNect.Core.Hid;
using Xunit;

namespace JoyNect.Core.Tests.Hid;

/// <summary>
/// Device path classification tests.
/// </summary>
/// <remarks>
/// The paths are real examples observed on a Windows 11 machine (a Pro Controller registered over
/// USB, a Nintendo HID interface, and an unrelated multi-collection device), because getting this
/// wrong means opening the wrong collection and reading nothing.
/// </remarks>
public sealed class HidDevicePathTests
{
    private const string ProControllerUsbPath =
        @"\\?\hid#vid_057e&pid_2009#7&35c399e5&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private const string ProControllerBluetoothPath =
        @"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002057e_pid&2009#a&2f9e0b1d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private const string MultiCollectionDeviceFirstCollection =
        @"\\?\hid#vid_30fa&pid_1701&mi_01&col02#7&5907fb6&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    private const string MultiCollectionDeviceSecondCollection =
        @"\\?\hid#vid_30fa&pid_1701&mi_01&col03#7&5907fb6&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Theory]
    [InlineData(ProControllerUsbPath, ConnectionKind.Usb)]
    [InlineData(ProControllerBluetoothPath, ConnectionKind.Bluetooth)]
    [InlineData(MultiCollectionDeviceFirstCollection, ConnectionKind.Usb)]
    [InlineData("", ConnectionKind.Unknown)]
    [InlineData(null, ConnectionKind.Unknown)]
    public void ClassifiesTheLinkType(string? path, ConnectionKind expected)
    {
        Assert.Equal(expected, HidDevicePath.Classify(path));
    }

    [Fact]
    public void ExtractsTheParentInstanceAndIgnoresTheCollectionIndex()
    {
        Assert.Equal("7&35c399e5", HidDevicePath.InstanceToken(ProControllerUsbPath));
        Assert.Equal("a&2f9e0b1d", HidDevicePath.InstanceToken(ProControllerBluetoothPath));

        // Both collections of one device must collapse onto the same parent instance.
        Assert.Equal(
            HidDevicePath.InstanceToken(MultiCollectionDeviceFirstCollection),
            HidDevicePath.InstanceToken(MultiCollectionDeviceSecondCollection));
    }

    [Fact]
    public void GroupsTheCollectionsOfOneDeviceButNotTwoDevices()
    {
        var first = HidDevicePath.PhysicalDeviceKey(MultiCollectionDeviceFirstCollection, 0x30FA, 0x1701);
        var second = HidDevicePath.PhysicalDeviceKey(MultiCollectionDeviceSecondCollection, 0x30FA, 0x1701);

        Assert.Equal(first, second);
        Assert.NotEqual(first, HidDevicePath.PhysicalDeviceKey(ProControllerUsbPath, 0x057E, 0x2009));
    }

    [Fact]
    public void ExtractsTheBluetoothAddressHint()
    {
        Assert.Equal(0x2f9e0b1dUL, HidDevicePath.TryBluetoothAddress(ProControllerBluetoothPath));
        Assert.Null(HidDevicePath.TryBluetoothAddress(ProControllerUsbPath));
    }

    [Fact]
    public void UnknownPathsProduceAnEmptyKey()
    {
        Assert.Equal(string.Empty, HidDevicePath.InstanceToken(null));
        Assert.Equal(string.Empty, HidDevicePath.InstanceToken("not-a-device-path"));
    }
}

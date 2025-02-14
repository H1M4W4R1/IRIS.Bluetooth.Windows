﻿# IRIS<sup>2</sup>: Intermediate Resource Integration System - Bluetooth for Windows Package
## Introduction
This is an [IRIS<sup>2</sup>](https://github.com/H1M4W4R1/IRIS) subpackage for Bluetooth communication on
Windows.

## Devices Abstractions
### BluetoothLowEnergyDeviceBase
Device used to communicate with BLE devices. Requires
BLE to be present on the system and uses WinRT API to
perform operations.

It takes one of two parameters in constructor:
* `string` - RegEx pattern to match device name (e.g.
  using prefix - `DEVICE-.*`)
* `Guid` - GATT service UUID to match device

Note: BLE devices are still W.I.P. and many things may
change, especially initialization of endpoints
(characteristics).

Examples:
```cs
public sealed class BluetoothLowEnergyMyDevice() :
    BluetoothLowEnergyDeviceBase(GattServiceUuids.HeartRate)
{
    // Code
}
```

```cs
public sealed class BluetoothLowEnergyMyDevice() :
    BluetoothLowEnergyDeviceBase("DEVICE-.*")
{
    // Code
}
```

## Communication Interfaces
### BluetoothLowEnergyInterface
Interface used to communicate with BLE devices. Uses WinRT
API to perform operations. Handles most communication
and is not recommended to be used directly.

## Included Examples
### BluetoothLowEnergyHeartRateBand
Example device implementation for BLE heart rate band that
connects to device with Heart Rate service.


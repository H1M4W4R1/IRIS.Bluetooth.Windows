# IRIS<sup>2</sup>: Intermediate Resource Integration System - Bluetooth for Windows Package

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

## BLE Endpoints
New part of the system is that it communicates with GATT services
and characteristics using endpoints. Endpoints are device-specific
information about location of specific data (or data channel) on the
device.

Endpoints can be loaded/attached either using GATT service UUID or
characteristic index. This allows to easily access data from the
device without need to know exact UUID of the service which in some cases
may be different on each device (e.g. sometimes heart rate bands use
different UUID for the characteristic, but it's almost always on the
index `0`).

Endpoints are identified by `uint` identifiers to be quickly accessible
from other parts of the system, however it's recommended to use value
returned from `AttachEndpoint` or `LoadEndpoint` methods and assign it
to custom property.

You need to override `AttachOrLoadEndpoints` method in your BLE device
implementation and attach or load endpoints to/from the device.

```cs
protected override void AttachOrLoadEndpoints()
{
    // Insert code here
}
```

### Attaching Endpoints
Attaching allows to automatically set notifications on the endpoint and attach
handler specific for the device. In case below it's handler used to parse heart
rate data.

```csharp
protected override void AttachOrLoadEndpoints()
{
       // Load heart rate endpoint and attach notification handler
    HeartRateEndpoint = await AttachEndpoint(HEART_RATE_ENDPOINT_ID, GattServiceUuids.HeartRate,
                HEART_RATE_CHARACTERISTIC_INDEX, HandleHeartRateNotification);
}
```

Now every time HeartRate service characteristic `0` changes it will call `HandleHeartRateNotification`
method.

### Loading Endpoints
Loading allows to load endpoints from the device. This is useful when you want
to create custom read/write operations on the device.

```csharp
protected override void AttachOrLoadEndpoints()
{
    // Load TX endpoint
    TXEndpoint = LoadEndpoint(BLE_TX_CHANNEL_ID, BLE_SERVICE_UUID, BLE_TX_CHARACTERISTIC_UUID);
   
    // Attach RX endpoint
    RXEndpoint = AttachEndpoint(BLE_RX_CHANNEL_ID, BLE_SERVICE_UUID, BLE_RX_CHARACTERISTIC_UUID, HandleRXNotification);
}
```

Above example bases on fact that some BLE devices use TX/RX channels to communicate
with the device. In this case we load TX channel and attach RX channel to
receive responses from the device.

It is recommended to wait for response after sending data to the device.

### Optional endpoints
By default, all endpoints loaded or attached are required. If you want to make
some endpoints optional you can use `AttachEndpoint` or `LoadEndpoint` method
with additional `EndpointMode.Optional` parameter (last one).

```csharp
protected override void AttachOrLoadEndpoints()
{
    // Load TX endpoint
    TXEndpoint = LoadEndpoint(BLE_TX_CHANNEL_ID, BLE_SERVICE_UUID, BLE_TX_CHARACTERISTIC_UUID);
   
    // Attach RX endpoint
    RXEndpoint = AttachEndpoint(BLE_RX_CHANNEL_ID, BLE_SERVICE_UUID, BLE_RX_CHARACTERISTIC_UUID, 
    HandleRXNotification, EndpointMode.Optional);
}
```

If optional endpoint is not found load/attach will return `null` and you can
check if endpoint is present in the device by comparing it to `null`.
### Attaching by potential endpoints
Attach and Load endpoint have overloads to be used with `PotentialEndpoint` table.
This allows for easy implementation of devices that have multiple endpoints with
different UUIDs doing the same thing, but being device-version specific.

```csharp
   protected async ValueTask<BluetoothLowEnergyEndpoint?> AttachEndpoint(
            uint endpointIndex,
            BluetoothLowEnergyEndpoint.NotificationReceivedHandler notificationHandler,
            EndpointMode mode = EndpointMode.Required,
            params PotentialEndpoint[] potentialEndpoints)
```

This methods have additional `params PotentialEndpoint[] potentialEndpoints` parameter
which allows to specify potential endpoints that can be used to attach endpoint.

```csharp
protected override void AttachOrLoadEndpoints()
{
    // Attach potential endpoints
    var potentialEndpoints = new PotentialEndpoint[]
    {
        // You can specify endpoint with single characteristic
        new PotentialEndpoint(SERVICE_A, TX_ENDPOINT_A),
        
        // Or with multiple characteristics (e.g. for different versions of the device)
        new PotentialEndpoint(SERVICE_B, TX_ENDPOINT_B, TX_ENDPOINT_C),
    };

    // Attach or load endpoint
    Endpoint = await AttachEndpoint(ENDPOINT_ID, HandleNotification, potentialEndpoints);
    Endpoint = LoadEndpoint(ENDPOINT_ID, potentialEndpoints);
}
```

## Included Examples
### BluetoothLowEnergyHeartRateBand
Example device implementation for BLE heart rate band that
connects to device with Heart Rate service.


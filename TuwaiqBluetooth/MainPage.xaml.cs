using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CoreBluetooth;
using CoreFoundation;
using Foundation;
using Microsoft.Maui.ApplicationModel;
using System.Threading.Tasks;

namespace TuwaiqBluetooth;

public partial class MainPage : ContentPage
{
	readonly ObservableCollection<string> devices = new();
	readonly HashSet<string> knownDevices = new();
	int unknownCount;
	CBCentralManager? centralManager;
	CentralManagerDelegate? centralDelegate;
	bool isScanning;
	bool requestedScan;

	public MainPage()
	{
		InitializeComponent();
		DevicesList.ItemsSource = devices;
	}

	void OnBluetoothToggled(object sender, ToggledEventArgs e)
	{
		requestedScan = e.Value;

		if (requestedScan)
		{
			EnsureCentralManager();
			TryStartScan();
		}
		else
		{
			StopScanning();
		}
	}

	void EnsureCentralManager()
	{
		if (centralDelegate == null)
			centralDelegate = new CentralManagerDelegate(AddDevice, OnCentralStateChanged);

		if (centralManager == null)
			centralManager = new CBCentralManager(centralDelegate, DispatchQueue.MainQueue);
		else
			centralManager.Delegate = centralDelegate;
	}

	void OnCentralStateChanged(CBManagerState state)
	{
		if (state == CBManagerState.PoweredOn)
			TryStartScan();
		else if (state == CBManagerState.PoweredOff)
			StopScanning();
	}

	void TryStartScan()
	{
		if (!requestedScan || centralManager?.State != CBManagerState.PoweredOn)
			return;

		devices.Clear();
		knownDevices.Clear();

		if (isScanning)
			centralManager!.StopScan();

		centralManager!.ScanForPeripherals(Array.Empty<CBUUID>());
		isScanning = true;
	}

	void StopScanning()
	{
		requestedScan = false;

		if (isScanning && centralManager != null)
			centralManager.StopScan();

		isScanning = false;
	}

	void AddDevice(CBPeripheral peripheral, NSDictionary advertisementData)
	{
		var label = ExtractLabel(peripheral, advertisementData);
		var identifier = Normalize(peripheral.Identifier?.AsString());
		var key = identifier ?? label;

		if (ShouldIgnoreEntry(label, identifier))
			return;

		if (string.IsNullOrEmpty(key) || !knownDevices.Add(key))
			return;

		var entry = FormatEntry(label, key);
		MainThread.BeginInvokeOnMainThread(() => devices.Add(entry));
	}

	static string? ExtractLabel(CBPeripheral peripheral, NSDictionary advertisementData)
	{
		var label = Normalize(peripheral.Name);

		if (!string.IsNullOrEmpty(label))
			return label;

		label = Normalize(advertisementData?[CBAdvertisement.DataLocalNameKey]?.ToString());

		return label;
	}

	static string? Normalize(string? raw)
	{
		return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
	}

	static bool ShouldIgnoreEntry(string? normalizedLabel, string? normalizedIdentifier)
	{
		var labelLooksRandom = LooksLikeRandomId(normalizedLabel);
		var idLooksRandom = LooksLikeRandomId(normalizedIdentifier);

		// Ignore entries that have no useful label or identifier.
		return labelLooksRandom && idLooksRandom;
	}

	static bool LooksLikeRandomId(string? value)
	{
		if (string.IsNullOrEmpty(value))
			return true;

		return Guid.TryParse(value, out _) || (value.Length >= 12 && IsHex(value));
	}

	string FormatEntry(string? label, string key)
	{
		var display = label ?? key;

		if (string.IsNullOrWhiteSpace(display))
			return $"Device {++unknownCount}";

		if (Guid.TryParse(display, out _) || (display.Length >= 12 && IsHex(display)))
			return $"Device {++unknownCount}";

		if (display.Length <= 20)
			return display;

		return display[..8] + "…" + display[^4..];
	}

	static bool IsHex(string value)
	{
		foreach (var ch in value)
		{
			if (!Uri.IsHexDigit(ch))
				return false;
		}

		return true;
	}

	class CentralManagerDelegate : CBCentralManagerDelegate
	{
		readonly Action<CBPeripheral, NSDictionary> onDeviceFound;
		readonly Action<CBManagerState> onStateChanged;

		public CentralManagerDelegate(Action<CBPeripheral, NSDictionary> handler, Action<CBManagerState> onState)
		{
			onDeviceFound = handler;
			onStateChanged = onState;
		}

		public override void UpdatedState(CBCentralManager central)
		{
			onStateChanged?.Invoke(central.State);
		}

		public override void DiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral, NSDictionary advertisementData, NSNumber RSSI)
		{
			onDeviceFound(peripheral, advertisementData);
		}
	}
}

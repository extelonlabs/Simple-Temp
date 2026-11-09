using LibreHardwareMonitor.Hardware;

namespace Temperature;

/// <summary>One set of values. A null value means the PC would not give it to us.</summary>
public sealed record Reading(
    string CpuName, float? CpuTemp, float? CpuLoad,
    string GpuName, float? GpuTemp, float? GpuLoad,
    float? RamLoad, float? RamUsedGb, float? RamTotalGb);

/// <summary>
/// Reads CPU, GPU and RAM values through LibreHardwareMonitor.
/// Build it and use it on one background thread: opening it can take a few seconds.
/// </summary>
public sealed class SensorReader : IDisposable
{
    private readonly Computer _computer;
    private readonly IHardware? _cpu;
    private readonly IHardware? _gpu;
    private readonly IHardware? _ram;

    public SensorReader()
    {
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
        _computer.Open();

        var all = _computer.Hardware;
        _cpu = all.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        // A PC can have two GPUs. The add-in card comes first, then the one built into the CPU.
        _gpu = all.FirstOrDefault(h => h.HardwareType == HardwareType.GpuNvidia)
            ?? all.FirstOrDefault(h => h.HardwareType == HardwareType.GpuAmd)
            ?? all.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);
        // The library lists "Virtual Memory" as memory too. We want the real RAM.
        _ram = all.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && !h.Name.Contains("Virtual"));
    }

    public Reading Read()
    {
        Update(_cpu);
        Update(_gpu);
        Update(_ram);

        float? used = Value(_ram, SensorType.Data, "Memory Used");
        float? free = Value(_ram, SensorType.Data, "Memory Available");

        return new Reading(
            _cpu?.Name ?? "CPU",
            CpuTemp(),
            Value(_cpu, SensorType.Load, "CPU Total"),
            _gpu?.Name ?? "GPU",
            GoodTemp(Value(_gpu, SensorType.Temperature, "GPU Core")),
            Value(_gpu, SensorType.Load, "GPU Core") ?? Value(_gpu, SensorType.Load, "D3D 3D"),
            Value(_ram, SensorType.Load, "Memory"),
            used,
            used + free);
    }

    public void Dispose() => _computer.Close();

    private float? CpuTemp()
    {
        // Best first: whole chip, then hottest core, then the top of whatever else there is.
        // "Distance to TjMax" is how far below the limit a core is, so it is not a real temperature.
        return GoodTemp(Value(_cpu, SensorType.Temperature, "CPU Package"))
            ?? GoodTemp(Value(_cpu, SensorType.Temperature, "Core Max"))
            ?? Sensors(_cpu)
                .Where(s => s.SensorType == SensorType.Temperature && !s.Name.Contains("Distance"))
                .Select(s => GoodTemp(s.Value))
                .Max();
    }

    /// <summary>A running chip is never at 0 °C or below. That just means no real data.</summary>
    private static float? GoodTemp(float? t) => t is > 0 and < 150 ? t : null;

    private static float? Value(IHardware? hardware, SensorType type, string name)
    {
        float? v = Sensors(hardware).FirstOrDefault(s => s.SensorType == type && s.Name == name)?.Value;
        return v is float f && float.IsNaN(f) ? null : v;
    }

    private static IEnumerable<ISensor> Sensors(IHardware? hardware) =>
        hardware is null
            ? []
            : hardware.Sensors.Concat(hardware.SubHardware.SelectMany(sub => sub.Sensors));

    private static void Update(IHardware? hardware)
    {
        if (hardware is null) return;
        hardware.Update();
        foreach (var sub in hardware.SubHardware) sub.Update();
    }
}

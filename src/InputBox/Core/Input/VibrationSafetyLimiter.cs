namespace InputBox.Core.Input;

/// <summary>
/// 震動安全限制器的診斷旗標。
/// </summary>
[Flags]
internal enum VibrationLimiterFlags
{
    /// <summary>
    /// 無額外狀態。
    /// </summary>
    None = 0,

    /// <summary>
    /// 因 Ambient 冷卻期間尚未結束而阻擋。
    /// </summary>
    BlockedByAmbientCooldown = 1 << 0,

    /// <summary>
    /// 因占空比超限而阻擋。
    /// </summary>
    BlockedByDutyCycle = 1 << 1,

    /// <summary>
    /// 因熱負載達硬上限而阻擋。
    /// </summary>
    BlockedByThermalHard = 1 << 2,

    /// <summary>
    /// 因估算熱負載即將溢出而阻擋。
    /// </summary>
    BlockedByThermalOverflow = 1 << 3,

    /// <summary>
    /// 因占空比壓力而縮放。
    /// </summary>
    ScaledByDutyCycle = 1 << 4,

    /// <summary>
    /// 因熱負載硬上限壓力而縮放。
    /// </summary>
    ScaledByThermalHard = 1 << 5,

    /// <summary>
    /// 因熱負載軟上限壓力而縮放。
    /// </summary>
    ScaledByThermalSoft = 1 << 6,

    /// <summary>
    /// 因優先級最低保底比例而縮放。
    /// </summary>
    ScaledByPriorityFloor = 1 << 7,

    /// <summary>
    /// 因可感知體驗保底而回補。
    /// </summary>
    ScaledByPerceptibilityFloor = 1 << 8
}

/// <summary>
/// 震動限制器單次決策的診斷資料快照。
/// </summary>
/// <param name="Accepted">是否接受本次震動請求。</param>
/// <param name="DutyCycle">目前視窗占空比（0 到 1）。</param>
/// <param name="ThermalLoad">目前熱負載估值。</param>
/// <param name="AppliedScale">本次套用的縮放倍率。</param>
/// <param name="Flags">本次決策命中的旗標集合。</param>
/// <param name="AmbientCooldownRemainingMs">Ambient 冷卻剩餘時間（毫秒）。</param>
internal readonly record struct VibrationLimiterDebugInfo(
    bool Accepted,
    double DutyCycle,
    double ThermalLoad,
    double AppliedScale,
    VibrationLimiterFlags Flags,
    long AmbientCooldownRemainingMs);

/// <summary>
/// 針對連續震動建立跨硬體的保護機制：占空比限制、熱負載衰減與優先級降級。
/// </summary>
internal sealed class VibrationSafetyLimiter
{
    private const ushort AmbientPerceptibleFloorStrength = 10_000;
    private const int AmbientPerceptibleFloorDurationMs = 35;

    private readonly Lock _lock = new();
    private readonly Queue<(long EndMs, int DurationMs)> _acceptedDurations = new();

    private readonly int _windowMs;
    private readonly double _maxDutyCycle;
    private readonly double _thermalSoftBudget;
    private readonly double _thermalHardBudget;
    private readonly double _thermalTauMs;
    private readonly int _ambientCooldownMs;

    private double _windowOnTimeMs;
    private double _thermalLoad;
    private long _lastSampleMs;
    private long _ambientCooldownUntilMs;

    /// <summary>
    /// 建立震動保護器。
    /// </summary>
    /// <param name="windowMs">占空比統計視窗（毫秒）。</param>
    /// <param name="maxDutyCycle">視窗允許的最大占空比，範圍為 0 到 1。</param>
    /// <param name="thermalSoftBudget">熱負載軟上限，超過後會開始降級。</param>
    /// <param name="thermalHardBudget">熱負載硬上限，超過後會強化抑制或拒絕。</param>
    /// <param name="thermalTauMs">熱負載指數衰減時間常數（毫秒）。</param>
    /// <param name="ambientCooldownMs">Ambient 被擋下後的冷卻時間（毫秒）。</param>
    public VibrationSafetyLimiter(
        int windowMs = 5000,
        double maxDutyCycle = 0.40,
        double thermalSoftBudget = 120.0,
        double thermalHardBudget = 180.0,
        double thermalTauMs = 2000.0,
        int ambientCooldownMs = 120)
    {
        _windowMs = Math.Max(windowMs, 100);
        _maxDutyCycle = Math.Clamp(maxDutyCycle, 0.05, 0.95);
        _thermalSoftBudget = Math.Max(thermalSoftBudget, 1.0);
        _thermalHardBudget = Math.Max(thermalHardBudget, _thermalSoftBudget + 1.0);
        _thermalTauMs = Math.Max(thermalTauMs, 100.0);
        _ambientCooldownMs = Math.Max(ambientCooldownMs, 0);
    }

    /// <summary>
    /// 依目前保護狀態嘗試套用震動請求，必要時自動縮減強度與持續時間。
    /// </summary>
    /// <param name="strength">原始強度（0 到 65535）。</param>
    /// <param name="durationMs">原始持續時間（毫秒）。</param>
    /// <param name="priority">震動優先級。</param>
    /// <param name="adjustedStrength">輸出調整後強度。</param>
    /// <param name="adjustedDurationMs">輸出調整後持續時間（毫秒）。</param>
    /// <returns>可接受時回傳 true；應被保護機制擋下時回傳 false。</returns>
    public bool TryApply(
        ushort strength,
        int durationMs,
        VibrationPriority priority,
        out ushort adjustedStrength,
        out int adjustedDurationMs)
    {
        return TryApply(
            strength,
            durationMs,
            priority,
            Environment.TickCount64,
            out adjustedStrength,
            out adjustedDurationMs,
            thermalCostMultiplier: 1.0);
    }

    /// <summary>
    /// 依目前保護狀態套用震動請求，並輸出診斷資料。
    /// </summary>
    /// <param name="strength">原始強度（0 到 65535）。</param>
    /// <param name="durationMs">原始持續時間（毫秒）。</param>
    /// <param name="priority">震動優先級。</param>
    /// <param name="adjustedStrength">輸出調整後強度。</param>
    /// <param name="adjustedDurationMs">輸出調整後持續時間（毫秒）。</param>
    /// <param name="diagnostics">輸出限制器診斷快照。</param>
    /// <returns>可接受時回傳 true；應被保護機制擋下時回傳 false。</returns>
    internal bool TryApplyWithDiagnostics(
        ushort strength,
        int durationMs,
        VibrationPriority priority,
        out ushort adjustedStrength,
        out int adjustedDurationMs,
        out VibrationLimiterDebugInfo diagnostics,
        double thermalCostMultiplier = 1.0)
    {
        return TryApplyWithDiagnostics(
            strength,
            durationMs,
            priority,
            Environment.TickCount64,
            out adjustedStrength,
            out adjustedDurationMs,
            out diagnostics,
            thermalCostMultiplier);
    }

    /// <summary>
    /// 供測試使用的時間可注入版本。
    /// </summary>
    /// <param name="strength">原始強度（0 到 65535）。</param>
    /// <param name="durationMs">原始持續時間（毫秒）。</param>
    /// <param name="priority">震動優先級。</param>
    /// <param name="nowMs">目前時間戳（毫秒）。</param>
    /// <param name="adjustedStrength">輸出調整後強度。</param>
    /// <param name="adjustedDurationMs">輸出調整後持續時間（毫秒）。</param>
    /// <returns>可接受時回傳 true；應被保護機制擋下時回傳 false。</returns>
    internal bool TryApply(
        ushort strength,
        int durationMs,
        VibrationPriority priority,
        long nowMs,
        out ushort adjustedStrength,
        out int adjustedDurationMs,
        double thermalCostMultiplier = 1.0)
    {
        return TryApplyWithDiagnostics(
            strength,
            durationMs,
            priority,
            nowMs,
            out adjustedStrength,
            out adjustedDurationMs,
            out _,
            thermalCostMultiplier);
    }

    /// <summary>
    /// 供測試或重播分析使用的時間可注入診斷版本。
    /// </summary>
    /// <param name="strength">原始強度（0 到 65535）。</param>
    /// <param name="durationMs">原始持續時間（毫秒）。</param>
    /// <param name="priority">震動優先級。</param>
    /// <param name="nowMs">目前時間戳（毫秒）。</param>
    /// <param name="adjustedStrength">輸出調整後強度。</param>
    /// <param name="adjustedDurationMs">輸出調整後持續時間（毫秒）。</param>
    /// <param name="diagnostics">輸出限制器診斷快照。</param>
    /// <returns>可接受時回傳 true；應被保護機制擋下時回傳 false。</returns>
    internal bool TryApplyWithDiagnostics(
        ushort strength,
        int durationMs,
        VibrationPriority priority,
        long nowMs,
        out ushort adjustedStrength,
        out int adjustedDurationMs,
        out VibrationLimiterDebugInfo diagnostics,
        double thermalCostMultiplier = 1.0)
    {
        adjustedStrength = 0;
        adjustedDurationMs = 0;
        diagnostics = new VibrationLimiterDebugInfo(
            Accepted: false,
            DutyCycle: 0.0,
            ThermalLoad: 0.0,
            AppliedScale: 0.0,
            Flags: VibrationLimiterFlags.None,
            AmbientCooldownRemainingMs: 0);

        if (strength == 0 ||
            durationMs <= 0)
        {
            return false;
        }

        int boundedDurationMs = Math.Clamp(durationMs, 1, 1000);

        lock (_lock)
        {
            DecayThermal(nowMs);
            PruneDutyWindow(nowMs);

            if (priority == VibrationPriority.Ambient &&
                nowMs < _ambientCooldownUntilMs)
            {
                diagnostics = new VibrationLimiterDebugInfo(
                    Accepted: false,
                    DutyCycle: _windowOnTimeMs / _windowMs,
                    ThermalLoad: _thermalLoad,
                    AppliedScale: 0.0,
                    Flags: VibrationLimiterFlags.BlockedByAmbientCooldown,
                    AmbientCooldownRemainingMs: Math.Max(0, _ambientCooldownUntilMs - nowMs));

                return false;
            }

            double scale = 1.0;
            VibrationLimiterFlags flags = VibrationLimiterFlags.None;

            double currentDuty = _windowOnTimeMs / _windowMs;

            if (currentDuty > _maxDutyCycle)
            {
                if (priority == VibrationPriority.Ambient)
                {
                    _ambientCooldownUntilMs = nowMs + _ambientCooldownMs;
                    flags |= VibrationLimiterFlags.BlockedByDutyCycle;
                    diagnostics = new VibrationLimiterDebugInfo(
                        Accepted: false,
                        DutyCycle: currentDuty,
                        ThermalLoad: _thermalLoad,
                        AppliedScale: 0.0,
                        Flags: flags,
                        AmbientCooldownRemainingMs: Math.Max(0, _ambientCooldownUntilMs - nowMs));

                    return false;
                }

                if (priority == VibrationPriority.Normal)
                {
                    scale *= 0.60;
                    flags |= VibrationLimiterFlags.ScaledByDutyCycle;
                }
            }

            if (_thermalLoad >= _thermalHardBudget)
            {
                if (priority == VibrationPriority.Ambient)
                {
                    _ambientCooldownUntilMs = nowMs + _ambientCooldownMs;
                    flags |= VibrationLimiterFlags.BlockedByThermalHard;
                    diagnostics = new VibrationLimiterDebugInfo(
                        Accepted: false,
                        DutyCycle: currentDuty,
                        ThermalLoad: _thermalLoad,
                        AppliedScale: 0.0,
                        Flags: flags,
                        AmbientCooldownRemainingMs: Math.Max(0, _ambientCooldownUntilMs - nowMs));

                    return false;
                }

                scale *= priority == VibrationPriority.Critical ? 0.75 : 0.50;
                flags |= VibrationLimiterFlags.ScaledByThermalHard;
            }
            else if (_thermalLoad > _thermalSoftBudget)
            {
                double thermalScale = Math.Sqrt(
                    Math.Clamp(
                        (_thermalHardBudget - _thermalLoad) / (_thermalHardBudget - _thermalSoftBudget),
                        0.0,
                        1.0));

                double floor = priority switch
                {
                    VibrationPriority.Critical => 0.75,
                    VibrationPriority.Normal => 0.55,
                    _ => 0.0
                };

                scale *= Math.Max(thermalScale, floor);
                flags |= VibrationLimiterFlags.ScaledByThermalSoft;
            }

            double minScale = priority switch
            {
                VibrationPriority.Critical => 0.45,
                VibrationPriority.Normal => 0.35,
                _ => 0.20
            };

            if (scale < minScale)
            {
                if (priority == VibrationPriority.Ambient)
                {
                    _ambientCooldownUntilMs = nowMs + _ambientCooldownMs;
                    flags |= VibrationLimiterFlags.BlockedByAmbientCooldown;
                    diagnostics = new VibrationLimiterDebugInfo(
                        Accepted: false,
                        DutyCycle: currentDuty,
                        ThermalLoad: _thermalLoad,
                        AppliedScale: scale,
                        Flags: flags,
                        AmbientCooldownRemainingMs: Math.Max(0, _ambientCooldownUntilMs - nowMs));

                    return false;
                }

                scale = minScale;
                flags |= VibrationLimiterFlags.ScaledByPriorityFloor;
            }

            ushort candidateStrength = (ushort)Math.Clamp(
                (int)Math.Round(strength * scale),
                1,
                ushort.MaxValue);

            int candidateDuration = Math.Clamp(
                (int)Math.Round(boundedDurationMs * scale),
                20,
                boundedDurationMs);

            // 在熱軟限制期間，為 Ambient 保留最低可感知脈衝，
            // 避免被降到「有發送但實際幾乎無感」的狀態。
            if (priority == VibrationPriority.Ambient &&
                (flags & VibrationLimiterFlags.ScaledByThermalSoft) != 0)
            {
                ushort ambientStrengthFloor = (ushort)Math.Min(strength, AmbientPerceptibleFloorStrength);
                int ambientDurationFloor = Math.Min(boundedDurationMs, AmbientPerceptibleFloorDurationMs);

                if (candidateStrength < ambientStrengthFloor)
                {
                    candidateStrength = ambientStrengthFloor;
                    flags |= VibrationLimiterFlags.ScaledByPerceptibilityFloor;
                }

                if (candidateDuration < ambientDurationFloor)
                {
                    candidateDuration = ambientDurationFloor;
                    flags |= VibrationLimiterFlags.ScaledByPerceptibilityFloor;
                }
            }

            double amplitude = candidateStrength / 65535.0;
            double clampedMultiplier = Math.Clamp(thermalCostMultiplier, 0.25, 8.0);
            double cost = amplitude * amplitude * candidateDuration * clampedMultiplier;

            if (priority != VibrationPriority.Critical &&
                _thermalLoad + cost > _thermalHardBudget * 1.05)
            {
                _ambientCooldownUntilMs = nowMs + _ambientCooldownMs;
                flags |= VibrationLimiterFlags.BlockedByThermalOverflow;
                diagnostics = new VibrationLimiterDebugInfo(
                    Accepted: false,
                    DutyCycle: currentDuty,
                    ThermalLoad: _thermalLoad,
                    AppliedScale: scale,
                    Flags: flags,
                    AmbientCooldownRemainingMs: Math.Max(0, _ambientCooldownUntilMs - nowMs));

                return false;
            }

            _thermalLoad += cost;
            _windowOnTimeMs += candidateDuration;
            _acceptedDurations.Enqueue((nowMs + candidateDuration, candidateDuration));

            adjustedStrength = candidateStrength;
            adjustedDurationMs = candidateDuration;

            diagnostics = new VibrationLimiterDebugInfo(
                Accepted: true,
                DutyCycle: currentDuty,
                ThermalLoad: _thermalLoad,
                AppliedScale: scale,
                Flags: flags,
                AmbientCooldownRemainingMs: Math.Max(0, _ambientCooldownUntilMs - nowMs));

            return true;
        }
    }

    /// <summary>
    /// 重置限制器的熱負載、占空比與冷卻狀態。
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _acceptedDurations.Clear();
            _windowOnTimeMs = 0.0;
            _thermalLoad = 0.0;
            _lastSampleMs = 0;
            _ambientCooldownUntilMs = 0;
        }
    }

    private void DecayThermal(long nowMs)
    {
        if (_lastSampleMs == 0)
        {
            _lastSampleMs = nowMs;

            return;
        }

        long elapsedMs = Math.Max(0, nowMs - _lastSampleMs);

        _lastSampleMs = nowMs;

        if (elapsedMs == 0)
        {
            return;
        }

        _thermalLoad *= Math.Exp(-elapsedMs / _thermalTauMs);
    }

    private void PruneDutyWindow(long nowMs)
    {
        long windowStart = nowMs - _windowMs;

        while (_acceptedDurations.Count > 0 &&
               _acceptedDurations.Peek().EndMs <= windowStart)
        {
            (long _, int durationMs) = _acceptedDurations.Dequeue();
            _windowOnTimeMs = Math.Max(0.0, _windowOnTimeMs - durationMs);
        }
    }
}
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
    ScaledByPerceptibilityFloor = 1 << 8,

    /// <summary>
    /// 單次請求超出剩餘熱預算，已降低強度以符合預算。
    /// </summary>
    ScaledByThermalOverflow = 1 << 9,

    /// <summary>
    /// 短時間內重複的相同 Critical 請求被視為自動連發，已降為 Normal 交由熱保護節流。
    /// </summary>
    DowngradedCriticalRepeat = 1 << 10
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
    /// <summary>
    /// 絕對震動強度安全上限。
    /// <para>即使呼叫端要求最大值，也會先裁切至較保守的硬體安全範圍。</para>
    /// </summary>
    private const ushort AbsoluteStrengthSafetyCap = 60_000;

    /// <summary>
    /// Ambient 類型震動的最低可感知強度。
    /// </summary>
    private const ushort AmbientPerceptibleFloorStrength = 10_000;

    /// <summary>
    /// 一般縮放後脈衝允許的最短持續時間（毫秒）。
    /// </summary>
    private const int MinimumScaledPulseDurationMs = 20;

    /// <summary>
    /// Ambient 類型震動的最低可感知持續時間（毫秒）。
    /// </summary>
    private const int AmbientPerceptibleFloorDurationMs = 35;

    /// <summary>
    /// 熱成本倍率的基準馬達數量（雙主馬達控制器，例如 XInput）。
    /// </summary>
    private const int ReferenceMotorCount = 2;

    /// <summary>
    /// 熱負載溢出判斷相對於硬上限的容許倍率。
    /// </summary>
    private const double ThermalOverflowTolerance = 1.05;

    /// <summary>
    /// 判定相同 Critical 請求為自動連發的時間視窗（毫秒）。
    /// </summary>
    /// <remarks>
    /// 例如按住方向鍵頂著邊界時，操作失敗回饋約每 150ms 觸發一次；若仍以 Critical 送出，
    /// 會不受熱保護限制地連續以高強度驅動馬達。視窗會隨每次連發延長，停手超過此時間後才恢復為 Critical。
    /// </remarks>
    private const int CriticalRepeatWindowMs = 500;

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
    private long _lastCriticalRequestMs = long.MinValue;
    private ushort _lastCriticalStrength;
    private int _lastCriticalDurationMs;

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
    /// 依控制器回報的震動馬達數量取得正規化的熱成本倍率。
    /// </summary>
    /// <remarks>
    /// 以雙主馬達為基準（倍率 1.0），讓預算與縮放參數在不同後端之間具有相同意義；
    /// 否則多馬達裝置的單次提示成本可能直接超過硬上限，導致冷啟動狀態下也被拒絕。
    /// </remarks>
    /// <param name="motorCount">支援的震動馬達數量；會被限制在 1 到 4 之間。</param>
    /// <returns>正規化後的熱成本倍率（0.5 到 2.0）。</returns>
    public static double GetMotorThermalCostMultiplier(int motorCount)
    {
        return Math.Clamp(motorCount, 1, 4) / (double)ReferenceMotorCount;
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
    /// <param name="thermalCostMultiplier">熱成本倍率，用於模擬不同馬達數量或驅動負載。</param>
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
    /// <param name="thermalCostMultiplier">熱成本倍率，用於模擬不同馬達數量或驅動負載。</param>
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
    /// <param name="thermalCostMultiplier">熱成本倍率，用於模擬不同馬達數量或驅動負載。</param>
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

            // 相同的 Critical 請求在連發視窗內再次出現時視為自動連發，降為 Normal 交由熱保護與占空比節流；
            // 第一次仍以 Critical 完整送出，強度或時長不同的 Critical 序列（例如控制器識別）不受影響。
            bool downgradedCriticalRepeat = false;

            if (priority == VibrationPriority.Critical)
            {
                bool isRepeat = _lastCriticalRequestMs != long.MinValue &&
                    nowMs - _lastCriticalRequestMs < CriticalRepeatWindowMs &&
                    _lastCriticalStrength == strength &&
                    _lastCriticalDurationMs == boundedDurationMs;

                _lastCriticalRequestMs = nowMs;
                _lastCriticalStrength = strength;
                _lastCriticalDurationMs = boundedDurationMs;

                if (isRepeat)
                {
                    priority = VibrationPriority.Normal;
                    downgradedCriticalRepeat = true;
                }
            }

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
            VibrationLimiterFlags flags = downgradedCriticalRepeat ?
                VibrationLimiterFlags.DowngradedCriticalRepeat :
                VibrationLimiterFlags.None;

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
                AbsoluteStrengthSafetyCap);

            int minimumDurationFloor = Math.Min(MinimumScaledPulseDurationMs, boundedDurationMs);
            int candidateDuration = Math.Clamp(
                (int)Math.Round(boundedDurationMs * scale),
                minimumDurationFloor,
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

            double clampedMultiplier = Math.Clamp(thermalCostMultiplier, 0.25, 8.0);
            double cost = ComputeThermalCost(candidateStrength, candidateDuration, clampedMultiplier);
            double overflowLimit = _thermalHardBudget * ThermalOverflowTolerance;

            // Normal 優先級單次請求超出剩餘熱預算時，先依剩餘預算降低振幅（熱成本與振幅平方成正比），
            // 只有降到優先級保底比例以下（代表馬達已接近過熱）才拒絕，避免冷啟動時重要回饋被整個吞掉。
            if (priority == VibrationPriority.Normal &&
                _thermalLoad + cost > overflowLimit)
            {
                double headroom = overflowLimit - _thermalLoad;
                double fitScale = headroom > 0.0 ?
                    Math.Sqrt(headroom / cost) :
                    0.0;

                if (scale * fitScale >= minScale)
                {
                    ushort fittedStrength = (ushort)Math.Max(1, (int)Math.Floor(candidateStrength * fitScale));

                    if (fittedStrength < candidateStrength)
                    {
                        candidateStrength = fittedStrength;
                        cost = ComputeThermalCost(candidateStrength, candidateDuration, clampedMultiplier);
                        scale *= fitScale;
                        flags |= VibrationLimiterFlags.ScaledByThermalOverflow;
                    }
                }
            }

            if (priority != VibrationPriority.Critical &&
                _thermalLoad + cost > overflowLimit)
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
    /// 計算單次震動的熱成本（振幅平方 × 持續時間 × 馬達倍率）。
    /// </summary>
    /// <param name="strength">震動強度（0 到 65535）。</param>
    /// <param name="durationMs">持續時間（毫秒）。</param>
    /// <param name="multiplier">已限制範圍的熱成本倍率。</param>
    /// <returns>熱成本估值。</returns>
    private static double ComputeThermalCost(ushort strength, int durationMs, double multiplier)
    {
        double amplitude = strength / 65535.0;

        return amplitude * amplitude * durationMs * multiplier;
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
            _lastCriticalRequestMs = long.MinValue;
            _lastCriticalStrength = 0;
            _lastCriticalDurationMs = 0;
        }
    }

    /// <summary>
    /// 依經過時間對熱負載進行指數衰減，模擬馬達冷卻。
    /// </summary>
    /// <param name="nowMs">目前時間戳（毫秒）。</param>
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

    /// <summary>
    /// 修剪超出統計視窗的歷史震動資料，維持最新占空比估算。
    /// </summary>
    /// <param name="nowMs">目前時間戳（毫秒）。</param>
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
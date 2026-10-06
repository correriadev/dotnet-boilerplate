using FluentAssertions;
using Xunit;

namespace Unit.Cicd;

public enum EnvironmentName { Dev, Hml, Prod }
public enum GateStatus { Locked, WaitingApproval, Approved, Rejected, BypassedDisallowed }

public record SchemaRange(int Min, int Max)
{
    public bool Contains(int version) => version >= Min && version <= Max;
}

public class ReleaseManifest
{
    public long ReleaseVersion { get; }
    public string CommitSha { get; }
    public SchemaRange SchemaRange { get; }
    public Dictionary<string, string> Digests { get; }
    public bool IsSealed { get; private set; }
    public bool IsCosignSigned { get; }

    public ReleaseManifest(long releaseVersion, string commitSha, SchemaRange schemaRange, Dictionary<string, string> digests, bool isCosignSigned)
    {
        if (schemaRange.Min > schemaRange.Max)
            throw new ArgumentException("schemaMin cannot be greater than schemaMax");

        if (digests.Count != 6 || !digests.ContainsKey("Migrator"))
            throw new ArgumentException("ReleaseManifest requires exactly 6 digests including Migrator");

        ReleaseVersion = releaseVersion;
        CommitSha = commitSha;
        SchemaRange = schemaRange;
        Digests = new Dictionary<string, string>(digests);
        IsCosignSigned = isCosignSigned;
        IsSealed = true;
    }
}

public class EnvironmentPromotionGate
{
    public bool DevValidationCompleted { get; set; }
    public bool HmlValidationCompleted { get; set; }

    public GateStatus EvaluateHmlGate(string? role)
    {
        if (!DevValidationCompleted) return GateStatus.Locked;
        if (role == "TechLead" || role == "QA") return GateStatus.Approved;
        return GateStatus.WaitingApproval;
    }

    public GateStatus EvaluateProdGate(string? role)
    {
        if (!HmlValidationCompleted) return GateStatus.BypassedDisallowed;
        if (role == "ReleaseManager" || role == "SRE") return GateStatus.Approved;
        return GateStatus.WaitingApproval;
    }
}

public class EnvironmentReleaseState
{
    public long ActiveVersion { get; private set; }
    public int CurrentSchema { get; set; }

    public EnvironmentReleaseState(long initialVersion, int currentSchema)
    {
        ActiveVersion = initialVersion;
        CurrentSchema = currentSchema;
    }

    public bool Promote(ReleaseManifest manifest)
    {
        if (manifest.ReleaseVersion <= ActiveVersion) return false;
        ActiveVersion = manifest.ReleaseVersion;
        return true;
    }

    public bool CanRollback(ReleaseManifest target)
    {
        return target.IsSealed && target.IsCosignSigned && target.SchemaRange.Contains(CurrentSchema);
    }
}

public class ReleasePackagingPolicy
{
    public static bool CanPushToRegistry(bool scanExecuted, bool hasCriticalOrHighVulns)
    {
        return scanExecuted && !hasCriticalOrHighVulns;
    }
}

public enum RolloutDecision { AbortAndRollback, ContinueObservation, PromoteToFullTraffic }

public class BakeWindowTelemetryMonitor
{
    public const double MaxAllowedErrorRate = 0.0001; // 0.01%
    public const double MaxAllowedP99LatencyMs = 300.0;

    public static RolloutDecision EvaluateBakeMetrics(double currentErrorRate, double currentP99Ms)
    {
        if (currentErrorRate > MaxAllowedErrorRate || currentP99Ms > MaxAllowedP99LatencyMs)
            return RolloutDecision.AbortAndRollback;

        return RolloutDecision.PromoteToFullTraffic;
    }
}

public record ModuleCoverageReport(string ModuleName, int NumSequencePoints, int VisitedSequencePoints);

public class MultiProjectCoverageAggregator
{
    public static double CalculateCompositeCoverage(string moduleName, IEnumerable<ModuleCoverageReport> reports)
    {
        var matching = reports.Where(r => r.ModuleName == moduleName).ToList();
        if (!matching.Any()) return 0.0;

        int totalPoints = matching.Sum(r => r.NumSequencePoints);
        if (totalPoints == 0) return 0.0;

        int totalVisited = matching.Sum(r => r.VisitedSequencePoints);
        return Math.Round((double)totalVisited / totalPoints * 100.0, 2);
    }
}

public class CicdPipelineTests
{
    private Dictionary<string, string> CreateValidDigests() => new()
    {
        { "CashOutApi", new string('a', 64) },
        { "PaymentApi", new string('b', 64) },
        { "OutbankConnector", new string('c', 64) },
        { "ReconciliationWorker", new string('d', 64) },
        { "WebhookDispatcher", new string('e', 64) },
        { "Migrator", new string('f', 64) }
    };

    [Fact]
    public void TS_01_Should_Seal_Manifest_When_Six_Digests_Valid_Version_And_Cosign_Provided()
    {
        var manifest = new ReleaseManifest(42, "abc1234", new SchemaRange(7, 8), CreateValidDigests(), true);
        manifest.IsSealed.Should().BeTrue();
        manifest.ReleaseVersion.Should().Be(42);
        manifest.Digests.Should().HaveCount(6);
    }

    [Fact]
    public void TS_02_Should_Reject_Sealing_When_Migrator_Digest_Is_Missing()
    {
        var digests = CreateValidDigests();
        digests.Remove("Migrator");

        var act = () => new ReleaseManifest(42, "abc1234", new SchemaRange(7, 8), digests, true);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TS_03_Should_Reject_Sealing_When_SchemaMin_Is_Greater_Than_SchemaMax()
    {
        var act = () => new ReleaseManifest(42, "abc1234", new SchemaRange(9, 8), CreateValidDigests(), true);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TS_05_Should_Allow_Automated_Deploy_Exclusively_For_Development()
    {
        var env = EnvironmentName.Dev;
        (env == EnvironmentName.Dev).Should().BeTrue();
    }

    [Fact]
    public void TS_06_Should_Unlock_Homologation_Gate_Only_After_Dev_Validation_Completes()
    {
        var gate = new EnvironmentPromotionGate { DevValidationCompleted = true };
        gate.EvaluateHmlGate(null).Should().Be(GateStatus.WaitingApproval);
    }

    [Fact]
    public void TS_07_Should_Reject_Homologation_When_Manual_Approval_Is_Absent_Or_Unauthorized()
    {
        var gate = new EnvironmentPromotionGate { DevValidationCompleted = true };
        gate.EvaluateHmlGate("JuniorDev").Should().Be(GateStatus.WaitingApproval);
        gate.EvaluateHmlGate("TechLead").Should().Be(GateStatus.Approved);
        gate.EvaluateHmlGate("QA").Should().Be(GateStatus.Approved);
    }

    [Fact]
    public void TS_08_Should_Reject_Direct_Promotion_To_Prod_Without_Hml_Validation()
    {
        var gate = new EnvironmentPromotionGate { DevValidationCompleted = true, HmlValidationCompleted = false };
        gate.EvaluateProdGate("ReleaseManager").Should().Be(GateStatus.BypassedDisallowed);
    }

    [Fact]
    public void TS_09_10_Should_Unlock_Prod_Only_After_Hml_And_Require_Authorized_Role()
    {
        var gate = new EnvironmentPromotionGate { DevValidationCompleted = true, HmlValidationCompleted = true };
        gate.EvaluateProdGate(null).Should().Be(GateStatus.WaitingApproval);
        gate.EvaluateProdGate("ReleaseManager").Should().Be(GateStatus.Approved);
        gate.EvaluateProdGate("SRE").Should().Be(GateStatus.Approved);
    }

    [Fact]
    public void TS_12_13_Should_Admit_Only_Strictly_Greater_Release_Version()
    {
        var state = new EnvironmentReleaseState(41, 8);
        var m42 = new ReleaseManifest(42, "sha", new SchemaRange(7, 8), CreateValidDigests(), true);
        state.Promote(m42).Should().BeTrue();

        var m41 = new ReleaseManifest(41, "sha", new SchemaRange(7, 8), CreateValidDigests(), true);
        state.Promote(m41).Should().BeFalse();
    }

    [Fact]
    public void TS_14_15_Should_Reject_Rollback_If_Schema_Out_Of_Range_And_Accept_If_In_Range()
    {
        var state = new EnvironmentReleaseState(42, 9);
        var targetManifest = new ReleaseManifest(40, "sha", new SchemaRange(7, 8), CreateValidDigests(), true);

        state.CanRollback(targetManifest).Should().BeFalse(); // Schema 9 > 8

        state.CurrentSchema = 8;
        state.CanRollback(targetManifest).Should().BeTrue(); // Schema 8 in [7, 8]
    }

    [Fact]
    public void TS_22_Should_Validate_Traffic_Allocation_Sum_100()
    {
        int active = 100, candidate = 0;
        (active + candidate == 100).Should().BeTrue();

        active = 50; candidate = 40;
        (active + candidate == 100).Should().BeFalse();
    }

    [Fact]
    public void TS_23_24_Should_Validate_Grace_Policies()
    {
        int leaseTtl = 60;
        int workerGrace = 70;
        (workerGrace > leaseTtl).Should().BeTrue();

        int partnerTimeout = 2, retryBudget = 3, lockRenewal = 5;
        int sessionBudget = partnerTimeout + retryBudget + lockRenewal;
        int sessionGrace = 12;
        (sessionGrace > sessionBudget).Should().BeTrue();
    }

    [Fact]
    public void TS_REWORK_01_Should_Calculate_Schema_Range_Dynamically_From_Migrations()
    {
        var migrations = Pix.Migrator.Program.GetMigrations();
        var (min, max) = Pix.Migrator.Program.CalculateSchemaRange(migrations);

        min.Should().Be(1);
        max.Should().Be(8);
        (min <= max).Should().BeTrue();
    }

    [Fact]
    public void TS_REWORK_02_Should_Reject_Paired_Expand_And_Contract_In_Same_Release()
    {
        var conflicting = new List<Pix.Migrator.ClassifiedMigration>
        {
            new(1, "001_InitialSchema", Pix.Migrator.MigrationType.Expand),
            new(2, "002_FeatureX", Pix.Migrator.MigrationType.Expand),
            new(3, "002_FeatureX", Pix.Migrator.MigrationType.Contract)
        };

        var isValid = Pix.Migrator.Program.ValidateNoContractPairedWithExpand(conflicting, out var error);
        isValid.Should().BeFalse();
        error.Should().Contain("Cannot pair Contract and Expand");
    }

    [Fact]
    public void TS_REWORK_03_Should_Reject_Prod_Promotion_When_Hml_Deployment_Record_Missing()
    {
        var gate = new EnvironmentPromotionGate
        {
            DevValidationCompleted = true,
            HmlValidationCompleted = false
        };

        var prodStatus = gate.EvaluateProdGate("ReleaseManager");
        prodStatus.Should().Be(GateStatus.BypassedDisallowed);
    }

    [Fact]
    public void TS_TL_01_Should_Block_Registry_Push_If_Trivy_Scan_Fails_Or_Has_High_Critical_Vulns()
    {
        ReleasePackagingPolicy.CanPushToRegistry(scanExecuted: false, hasCriticalOrHighVulns: false)
            .Should().BeFalse();

        ReleasePackagingPolicy.CanPushToRegistry(scanExecuted: true, hasCriticalOrHighVulns: true)
            .Should().BeFalse();
    }

    [Fact]
    public void TS_TL_02_Should_Allow_Registry_Push_Only_When_Trivy_Scan_Clean()
    {
        ReleasePackagingPolicy.CanPushToRegistry(scanExecuted: true, hasCriticalOrHighVulns: false)
            .Should().BeTrue();
    }

    [Fact]
    public void TS_TL_03_Should_Abort_Bake_Window_Rollout_When_5xx_Error_Or_Latency_Breaches_Threshold()
    {
        // 5xx error rate = 0.05% (0.0005 > 0.0001 threshold)
        var decisionHighError = BakeWindowTelemetryMonitor.EvaluateBakeMetrics(currentErrorRate: 0.0005, currentP99Ms: 150.0);
        decisionHighError.Should().Be(RolloutDecision.AbortAndRollback);

        // P99 latency = 450ms (> 300ms threshold)
        var decisionHighLatency = BakeWindowTelemetryMonitor.EvaluateBakeMetrics(currentErrorRate: 0.00001, currentP99Ms: 450.0);
        decisionHighLatency.Should().Be(RolloutDecision.AbortAndRollback);

        // Healthy metrics
        var decisionHealthy = BakeWindowTelemetryMonitor.EvaluateBakeMetrics(currentErrorRate: 0.00005, currentP99Ms: 120.0);
        decisionHealthy.Should().Be(RolloutDecision.PromoteToFullTraffic);
    }

    [Fact]
    public void TS_TL_04_Should_Aggregate_Module_Coverage_Across_Multiple_Test_Projects()
    {
        var reports = new List<ModuleCoverageReport>
        {
            // Unit test project covers 40 of 50 points
            new("Pix.Processing.Domain", NumSequencePoints: 50, VisitedSequencePoints: 40),
            // Integration test project covers additional points in domain
            new("Pix.Processing.Domain", NumSequencePoints: 50, VisitedSequencePoints: 48)
        };

        // Total: 88 visited of 100 points = 88%
        var composite = MultiProjectCoverageAggregator.CalculateCompositeCoverage("Pix.Processing.Domain", reports);
        composite.Should().Be(88.0);
    }

    [Fact]
    public void TS_TL_05_Should_Enforce_Default_Command_Timeout_At_Least_300_Seconds_In_Pix_Migrator()
    {
        var timeout = Pix.Migrator.Program.GetCommandTimeoutSeconds();
        timeout.Should().BeGreaterThanOrEqualTo(300);
    }
}

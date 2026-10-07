using FluentAssertions;
using Pix.Processing.Domain.Telemetry;
using System.Diagnostics;
using Xunit;

namespace Unit;

public class TelemetryTests
{
    [Fact]
    public void TS_OBS_01_Should_Emit_Pass_Verdict_When_All_Golden_Signals_Meet_Thresholds()
    {
        var release = new ReleaseMetadata("v1.0.0", "sha123", "digest123", "hml");
        var laudo = new LaudoGuardian("eval-1", release, DateTimeOffset.UtcNow);
        laudo.ConcluirAvaliacao(Array.Empty<ViolacaoSLO>());

        laudo.Veredito.Should().Be(VereditoQualityGate.Pass);
        laudo.Status.Should().Be("Concluido");
    }

    [Fact]
    public void TS_OBS_02_Should_Emit_Fail_Verdict_When_P95_Latency_Exceeds_Threshold()
    {
        var release = new ReleaseMetadata("v1.0.0", "sha123", "digest123", "hml");
        var laudo = new LaudoGuardian("eval-2", release, DateTimeOffset.UtcNow);
        var violacoes = new List<ViolacaoSLO> { new("LatenciaP95", 480.0, 400.0, "Latência P95 violada") };
        laudo.ConcluirAvaliacao(violacoes);

        laudo.Veredito.Should().Be(VereditoQualityGate.Fail);
    }

    [Fact]
    public void TS_OBS_03_Should_Emit_Fail_Verdict_When_BACEN_SPI_Latency_Exceeds_2s()
    {
        var release = new ReleaseMetadata("v1.0.0", "sha123", "digest123", "hml");
        var laudo = new LaudoGuardian("eval-3", release, DateTimeOffset.UtcNow);
        var violacoes = new List<ViolacaoSLO> { new("LatenciaSPIBacen", 2300.0, 2000.0, "SLA Bacen violado") };
        laudo.ConcluirAvaliacao(violacoes);

        laudo.Veredito.Should().Be(VereditoQualityGate.Fail);
    }

    [Fact]
    public void TS_OBS_04_Should_Reject_Modification_Once_Guardian_Report_Is_Sealed()
    {
        var release = new ReleaseMetadata("v1.0.0", "sha123", "digest123", "hml");
        var laudo = new LaudoGuardian("eval-4", release, DateTimeOffset.UtcNow);
        laudo.ConcluirAvaliacao(Array.Empty<ViolacaoSLO>());

        var act = () => laudo.ConcluirAvaliacao(Array.Empty<ViolacaoSLO>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*imutável*");
    }

    [Fact]
    public void TS_OBS_05_Should_Complete_Bake_Inspection_Successfully_Without_Anomalies()
    {
        var agora = DateTimeOffset.UtcNow;
        var inspecao = new InspecaoBakePeriod("rev-01", TimeSpan.FromMinutes(10), agora);
        inspecao.FinalizarInspecao(agora.AddMinutes(10));

        inspecao.Status.Should().Be("Estavel");
    }

    [Fact]
    public void TS_OBS_06_Should_Transition_To_Degraded_When_Davis_AI_Detects_Anomaly()
    {
        var agora = DateTimeOffset.UtcNow;
        var inspecao = new InspecaoBakePeriod("rev-01", TimeSpan.FromMinutes(10), agora);
        inspecao.RegistrarAnomaliaDavis("DAVIS-001", "High Error Rate");

        inspecao.Status.Should().Be("Degradado");
        inspecao.Anomalias.Should().ContainSingle();
    }

    [Fact]
    public void TS_OBS_07_Should_Create_ContextoW3C_Successfully_When_Valid()
    {
        var header = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var ctx = ContextoW3C.Create(header, "congo=t61rcWkgMzE");

        ctx.TraceParent.Should().Be(header);
        ctx.TraceId.Should().Be("4bf92f3577b34da6a3ce929d0e0e4736");
        ctx.SpanId.Should().Be("00f067aa0ba902b7");
    }

    [Fact]
    public void TS_OBS_08_Should_Reject_Creation_When_TraceParent_Invalid()
    {
        var act = () => ContextoW3C.Create("invalid-traceparent");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TS_OBS_09_Should_Validate_Compliance_With_Bacen_Sla()
    {
        var signals = new MetricasGoldenSignals(200.0, 1950.0, 0.01, 100.0);
        signals.AtendeSlaBacen().Should().BeTrue();
    }

    [Fact]
    public void TS_OBS_10_Should_Reject_Negative_Or_Out_Of_Bounds_Metrics()
    {
        var act = () => new MetricasGoldenSignals(200.0, 1000.0, -5.0, 100.0);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TS_OBS_11_Should_Accept_Valid_IdentificadorTecnico()
    {
        var id = Guid.NewGuid();
        var e2e = "E1234567820261007000000000000001";
        var tecnico = IdentificadorTecnico.Validar(id, e2e);

        tecnico.PedidoId.Should().Be(id);
        tecnico.EndToEndId.Should().Be(e2e);
    }

    [Fact]
    public void TS_OBS_12_Should_Reject_IdentificadorTecnico_Containing_Pii()
    {
        var id = Guid.NewGuid();
        var act = () => IdentificadorTecnico.Validar(id, "123.456.789-00123456789012345678");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TS_OBS_13_Should_Sanitize_Pii_And_Keep_Correlation_Keys()
    {
        using var act = new Activity("TestActivity").Start();
        act.SetTag("cpf", "123.456.789-00");
        act.SetTag("chave_pix", "user@domain.com");

        var ids = IdentificadorTecnico.Validar(Guid.NewGuid(), "E1234567820261007000000000000001");
        SanitizadorTelemetriaPolicy.AplicarTagsSeguras(act, ids);

        act.GetTagItem("cpf").Should().BeNull();
        act.GetTagItem("chave_pix").Should().BeNull();
        act.GetTagItem("pedido_id").Should().Be(ids.PedidoId.ToString());
        act.GetTagItem("end_to_end_id").Should().Be(ids.EndToEndId);
    }

    [Fact]
    public void TS_OBS_14_Should_Detect_Sensitive_Account_Patterns()
    {
        SanitizadorTelemetriaPolicy.ContemDadosSensiveis("conta_corrente").Should().BeTrue();
        SanitizadorTelemetriaPolicy.ContemDadosSensiveis("agencia").Should().BeTrue();
        SanitizadorTelemetriaPolicy.ContemDadosSensiveis("pedido_id").Should().BeFalse();
    }

    [Fact]
    public void TS_OBS_15_Should_Reject_All_Zero_TraceId_Or_SpanId_In_ContextoW3C()
    {
        var allZeroTrace = "00-00000000000000000000000000000000-00f067aa0ba902b7-01";
        var actTrace = () => ContextoW3C.Create(allZeroTrace);
        actTrace.Should().Throw<ArgumentException>().WithMessage("*zeros*");

        var allZeroSpan = "00-4bf92f3577b34da6a3ce929d0e0e4736-0000000000000000-01";
        var actSpan = () => ContextoW3C.Create(allZeroSpan);
        actSpan.Should().Throw<ArgumentException>().WithMessage("*zeros*");
    }

    [Fact]
    public void TS_OBS_16_Should_Reject_Inverted_Percentiles_And_NaN_In_MetricasGoldenSignals()
    {
        // P99 menor que P95
        var actInverted = () => new MetricasGoldenSignals(400.0, 350.0, 0.01, 100.0);
        actInverted.Should().Throw<ArgumentException>().WithMessage("*Invariante de percentil*");

        // NaN em latência
        var actNan = () => new MetricasGoldenSignals(double.NaN, 500.0, 0.01, 100.0);
        actNan.Should().Throw<ArgumentException>();

        // Infinity em taxa de erro
        var actInf = () => new MetricasGoldenSignals(200.0, 400.0, double.PositiveInfinity, 100.0);
        actInf.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TS_OBS_17_Should_Emit_Fail_Verdict_For_English_Dynatrace_SLO_Violations()
    {
        var release = new ReleaseMetadata("v1.0.0", "sha123", "digest123", "hml");
        var laudo = new LaudoGuardian("eval-english-1", release, DateTimeOffset.UtcNow);
        var violacoes = new List<ViolacaoSLO>
        {
            new("response_time_p95", 450.0, 400.0, "Response time P95 breached"),
            new("failure_rate", 2.5, 0.5, "Failure rate breached")
        };
        laudo.ConcluirAvaliacao(violacoes);

        laudo.Veredito.Should().Be(VereditoQualityGate.Fail);
    }

    [Fact]
    public void TS_OBS_18_Should_Reject_Invalid_Bacen_EndToEndId_Structure_Or_Raw_Cpf()
    {
        var id = Guid.NewGuid();

        // Prefixo inválido (deve começar com E ou M)
        var actPrefix = () => IdentificadorTecnico.Validar(id, "X1234567820261007000000000000001");
        actPrefix.Should().Throw<ArgumentException>().WithMessage("*BACEN inválido*");

        // Tamanho incorreto (não possui 32 caracteres)
        var actLen = () => IdentificadorTecnico.Validar(id, "E12345678");
        actLen.Should().Throw<ArgumentException>().WithMessage("*BACEN inválido*");

        // CPF sem formatação passado como identificador
        var actCpf = () => IdentificadorTecnico.Validar(id, "12345678901");
        actCpf.Should().Throw<ArgumentException>().WithMessage("*informação pessoal identificável*");

        // CPF preenchido com zeros à esquerda
        var actCpfZeros = () => IdentificadorTecnico.Validar(id, "00012345678");
        actCpfZeros.Should().Throw<ArgumentException>().WithMessage("*informação pessoal identificável*");

        // E-mail passado como identificador
        var actEmail = () => IdentificadorTecnico.Validar(id, "cliente@pix.com.br");
        actEmail.Should().Throw<ArgumentException>().WithMessage("*informação pessoal identificável*");
    }

    [Fact]
    public void TS_OBS_19_Should_Sanitize_Boxed_Numeric_And_Phone_Tags_In_Activity()
    {
        using var act = new Activity("TestActivityBoxed").Start();
        act.SetTag("conta", 12345678901L);
        act.SetTag("telefone", "+5511999998888");
        act.SetTag("token", "secret-token-value");

        var ids = IdentificadorTecnico.Validar(Guid.NewGuid(), "E1234567820261007000000000000001");
        SanitizadorTelemetriaPolicy.AplicarTagsSeguras(act, ids);

        act.GetTagItem("conta").Should().BeNull();
        act.GetTagItem("telefone").Should().BeNull();
        act.GetTagItem("token").Should().BeNull();
        act.GetTagItem("pedido_id").Should().Be(ids.PedidoId.ToString());
    }

    [Fact]
    public void TS_OBS_20_Should_Handle_Default_ContextoW3C_Struct_Safely()
    {
        var defaultCtx = default(ContextoW3C);
        defaultCtx.TraceParent.Should().BeNull();
        defaultCtx.TraceId.Should().BeEmpty();
        defaultCtx.SpanId.Should().BeEmpty();
    }

    [Fact]
    public void TS_OBS_21_Should_Sanitize_Dictionary_Of_Tags()
    {
        var tags = new Dictionary<string, object?>
        {
            { "safe_key", "public_value" },
            { "senha", "password123" },
            { "user_email", "admin@domain.com" },
            { "correlation_id", "corr-123" }
        };

        var sanitized = SanitizadorTelemetriaPolicy.SanitizarTags(tags);
        sanitized.Should().ContainKey("safe_key");
        sanitized.Should().ContainKey("correlation_id");
        sanitized.Should().NotContainKey("senha");
        sanitized.Should().NotContainKey("user_email");
    }
}

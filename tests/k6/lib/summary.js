import { environment, testType } from './config.js';

export function handleSummary(data) {
  const metrics = data.metrics || {};
  const duration = metrics.http_req_duration?.values || {};
  const checks = metrics.checks?.values?.rate;
  const failures = metrics.http_req_failed?.values?.rate;
  const totalRequests = metrics.http_reqs?.values?.count || 0;

  const formatPercent = (val) =>
    val === undefined ? 'N/A' : `${(val * 100).toFixed(2)}%`;

  const formatMs = (val) =>
    val === undefined ? 'N/A' : `${val.toFixed(2)} ms`;

  const thresholdRows = Object.entries(metrics).flatMap(([metricName, metricData]) => {
    const thresholds = metricData.thresholds || {};
    return Object.entries(thresholds).map(([rule, result]) => {
      const statusIcon = result.ok ? 'PASS' : 'FAIL';
      return `| \`${metricName}\` | \`${rule}\` | ${statusIcon} |`;
    });
  });

  const markdown = [
    `# Relatório de Performance k6 — \`${environment.toUpperCase()}\` / \`${testType.toUpperCase()}\``,
    '',
    `**Data/Hora da Execução:** ${new Date().toISOString()}`,
    '',
    '## Métricas Principais de Execução',
    '',
    '| Métrica | Valor Obtido | Status / Observação |',
    '| :--- | :--- | :--- |',
    `| Total de Requisições | ${totalRequests} | Volume processado |`,
    `| Taxa de Sucesso nos Checks | ${formatPercent(checks)} | Meta: 100% |`,
    `| Taxa de Falhas HTTP | ${formatPercent(failures)} | Meta: < 1.00% |`,
    `| Latência Média | ${formatMs(duration.avg)} | - |`,
    `| Latência Mediana (P50) | ${formatMs(duration.med)} | - |`,
    `| Latência P95 | ${formatMs(duration['p(95)'])} | SLO Target: < 200 ms |`,
    `| Latência P99 | ${formatMs(duration['p(99)'])} | SLO Target: < 500 ms |`,
    `| Latência Máxima | ${formatMs(duration.max)} | Pico registrado |`,
    '',
    '## Avaliação de Portas de Qualidade (Thresholds)',
    '',
    '| Métrica | Regra de Corte (Threshold) | Resultado |',
    '| :--- | :--- | :--- |',
    ...(thresholdRows.length > 0 ? thresholdRows : ['| Nenhum threshold configurado | - | N/A |']),
    '',
  ].join('\n');

  return {
    stdout: `${markdown}\n`,
    'summary.md': markdown,
    'summary.json': JSON.stringify(data, null, 2),
  };
}


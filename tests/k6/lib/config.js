export const environment = __ENV.ENVIRONMENT || 'dev';
export const testType = __ENV.TEST_TYPE || 'smoke';
export const baseUrl = (__ENV.BASE_URL || 'http://localhost:5000').replace(/\/+$/, '');

const validEnvironments = ['local', 'dev', 'hml', 'prod'];
const validTestTypes = ['smoke', 'load', 'stress'];

if (!validEnvironments.includes(environment.toLowerCase())) {
  throw new Error(`ENVIRONMENT inválido (${environment}). Deve ser: ${validEnvironments.join(', ')}`);
}

if (!validTestTypes.includes(testType.toLowerCase())) {
  throw new Error(`TEST_TYPE inválido (${testType}). Deve ser: ${validTestTypes.join(', ')}`);
}

if (!/^https?:\/\/[^/\s?#]+(?:\/[^?\s#]*)?$/.test(baseUrl)) {
  throw new Error(`BASE_URL inválida (${baseUrl}). Deve ser uma URL HTTP/HTTPS válida sem querystrings.`);
}

function resolveScenario(type) {
  switch (type.toLowerCase()) {
    case 'smoke':
      return {
        executor: 'constant-vus',
        vus: 2,
        duration: '30s',
        gracefulStop: '5s',
      };
    case 'load':
      return {
        executor: 'ramping-vus',
        startVUs: 0,
        stages: [
          { duration: '30s', target: 10 },
          { duration: '1m30s', target: 20 },
          { duration: '30s', target: 0 },
        ],
        gracefulRampDown: '10s',
        gracefulStop: '10s',
      };
    case 'stress':
      return {
        executor: 'ramping-vus',
        startVUs: 0,
        stages: [
          { duration: '30s', target: 20 },
          { duration: '1m', target: 50 },
          { duration: '30s', target: 0 },
        ],
        gracefulRampDown: '10s',
        gracefulStop: '10s',
      };
    default:
      throw new Error(`Cenário não suportado: ${type}`);
  }
}

function resolveThresholds(type) {
  const base = {
    http_reqs: ['count>0'],
    checks: ['rate==1.0'],
    http_req_failed: ['rate<0.01'],
  };

  if (type.toLowerCase() === 'load') {
    base.http_req_duration = ['p(95)<200', 'p(99)<500'];
  } else if (type.toLowerCase() === 'stress') {
    base.http_req_duration = ['p(95)<400', 'p(99)<1000'];
  }

  return base;
}

export const options = {
  scenarios: {
    health_check_scenario: resolveScenario(testType),
  },
  tags: {
    environment: environment.toLowerCase(),
    test_type: testType.toLowerCase(),
  },
  summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(95)', 'p(99)'],
  thresholds: resolveThresholds(testType),
};


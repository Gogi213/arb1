const https = require('https');

const API_KEY = 'sm_9ir8yzmmBVo3dB6DbZNDiQ_YpgRmTFlYUSwImkitsyoyqkRvuUfBzxuOcFhFPtXzUhGelETapNOkeKWThvAXUGX';
const BASE_URL = 'https://api.supermemory.ai/v3';

async function makeRequest(method, path, data) {
  return new Promise((resolve, reject) => {
    const url = new URL(path, BASE_URL);
    const options = {
      hostname: url.hostname,
      port: 443,
      path: url.pathname + url.search,
      method: method,
      headers: {
        'Authorization': `Bearer ${API_KEY}`,
        'Content-Type': 'application/json',
      },
    };

    const req = https.request(options, (res) => {
      let body = '';
      res.on('data', (chunk) => {
        body += chunk;
      });
      res.on('end', () => {
        try {
          const response = JSON.parse(body);
          resolve(response);
        } catch (e) {
          resolve({ error: body });
        }
      });
    });

    req.on('error', (error) => {
      reject(error);
    });

    if (data) {
      req.write(JSON.stringify(data));
    }
    req.end();
  });
}

async function testSupermemory() {
  console.log('Testing Supermemory API...');

  // Test adding memory
  console.log('\n1. Adding memory...');
  try {
    const addResponse = await makeRequest('POST', '/documents', {
      content: 'Это реальный тест Supermemory API. Сервер настроен корректно и работает с новым API ключом.',
      metadata: {
        type: 'test',
        source: 'direct-api-test',
        language: 'russian'
      },
      containerTags: ['test', 'api-test', 'russian']
    });
    console.log('✅ Memory added successfully:', addResponse);
  } catch (error) {
    console.log('❌ Error adding memory:', error.message);
  }

  // Wait a bit for processing
  await new Promise(resolve => setTimeout(resolve, 2000));

  // Test searching
  console.log('\n2. Searching memories...');
  try {
    const searchResponse = await makeRequest('POST', '/search', {
      q: 'Supermemory API тест',
      limit: 5,
      threshold: 0.3
    });
    console.log('✅ Search results:', searchResponse);
  } catch (error) {
    console.log('❌ Error searching:', error.message);
  }

  // Test listing
  console.log('\n3. Listing memories...');
  try {
    const listResponse = await makeRequest('POST', '/documents/list', {
      limit: 5,
      page: 1
    });
    console.log('✅ List results:', listResponse);
  } catch (error) {
    console.log('❌ Error listing:', error.message);
  }
}

testSupermemory().catch(console.error);

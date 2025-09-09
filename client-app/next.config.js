/** @type {import('next').NextConfig} */
const nextConfig = {
  env: {
    AUTH_SERVICE_URL: process.env.AUTH_SERVICE_URL || 'http://auth-service:8080',
    WEB_SERVICE_URL: process.env.WEB_SERVICE_URL || 'http://web-service:8080',
  },
};

module.exports = nextConfig;

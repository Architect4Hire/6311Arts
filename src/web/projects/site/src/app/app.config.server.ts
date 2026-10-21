import { HttpBackend } from '@angular/common/http';
import {
  ApplicationConfig,
  TransferState,
  inject,
  mergeApplicationConfig,
  provideEnvironmentInitializer,
} from '@angular/core';
import { provideServerRendering, withRoutes } from '@angular/ssr';
import { appConfig } from './app.config';
import { serverRoutes } from './app.routes.server';
import { GatewayBackend, SITE_API_ORIGIN } from './core/gateway-backend';
import {
  MOCK_FALLBACK_KEY,
  SITE_API_CONFIG,
  SITE_API_TIMEOUT_MS,
} from './services/site-api.config';

/** Read once per render from the Node server's environment; the browser bundle never contains these. */
const mockFallback = () => process.env['SITE_MOCK_FALLBACK']?.trim().toLowerCase() === 'true';

const serverConfig: ApplicationConfig = {
  providers: [
    provideServerRendering(withRoutes(serverRoutes)),
    // Site API reads go to the gateway, the only way into the API (ADR 0001); see GatewayBackend.
    { provide: SITE_API_ORIGIN, useFactory: () => process.env['SITE_API_ORIGIN'] || null },
    { provide: HttpBackend, useClass: GatewayBackend },
    {
      provide: SITE_API_CONFIG,
      useFactory: () => ({ timeoutMs: SITE_API_TIMEOUT_MS, mockFallback: mockFallback() }),
    },
    // Every page carries the flag, so a later client-side navigation falls back (or doesn't) as the render did.
    provideEnvironmentInitializer(() =>
      inject(TransferState).set(MOCK_FALLBACK_KEY, mockFallback()),
    ),
  ],
};

export const config = mergeApplicationConfig(appConfig, serverConfig);

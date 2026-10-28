// Generates the colour fixtures that verify JoyNect's C# Material 3 port.
//
// The reference values come from the official Material Color Utilities package rather than from
// hand-written expectations, so a mistake in the port shows up as a numeric difference instead of
// being baked into the test.
//
// Run with: npm install && npm run generate

import { writeFileSync, mkdirSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import {
  Hct,
  TonalPalette,
  SchemeTonalSpot,
  SchemeExpressive,
  MaterialDynamicColors,
  DynamicScheme,
  argbFromHex,
  hexFromArgb,
} from '@material/material-color-utilities';

const here = dirname(fileURLToPath(import.meta.url));
const outputDirectory = join(here, '..', '..', 'tests', 'JoyNect.Core.Tests', 'TestData');

// Seeds chosen for coverage: the Material baseline purple, a highly chromatic red, a low-chroma
// grey (where chroma reduction is exercised hardest), and a green.
const seeds = ['#6750A4', '#B3261E', '#8F8F94', '#2E7D32'];

const tones = [0, 5, 10, 20, 30, 40, 50, 60, 70, 80, 90, 95, 99, 100];

// The roles JoyNect uses, in the order the C# enum declares them.
const roles = [
  'primary', 'onPrimary', 'primaryContainer', 'onPrimaryContainer',
  'primaryFixed', 'primaryFixedDim', 'onPrimaryFixed', 'onPrimaryFixedVariant',
  'secondary', 'onSecondary', 'secondaryContainer', 'onSecondaryContainer',
  'secondaryFixed', 'secondaryFixedDim', 'onSecondaryFixed', 'onSecondaryFixedVariant',
  'tertiary', 'onTertiary', 'tertiaryContainer', 'onTertiaryContainer',
  'tertiaryFixed', 'tertiaryFixedDim', 'onTertiaryFixed', 'onTertiaryFixedVariant',
  'error', 'onError', 'errorContainer', 'onErrorContainer',
  'surface', 'onSurface', 'surfaceVariant', 'onSurfaceVariant',
  'surfaceDim', 'surfaceBright',
  'surfaceContainerLowest', 'surfaceContainerLow', 'surfaceContainer', 'surfaceContainerHigh', 'surfaceContainerHighest',
  'inverseSurface', 'inverseOnSurface', 'inversePrimary',
  'outline', 'outlineVariant', 'shadow', 'scrim', 'background', 'onBackground', 'surfaceTint',
];

function roleHex(scheme, role) {
  const dynamicColor = MaterialDynamicColors[role];
  if (!dynamicColor) {
    throw new Error(`unknown role: ${role}`);
  }
  return hexFromArgb(dynamicColor.getArgb(scheme));
}

function schemeFixture(scheme) {
  const result = {};
  for (const role of roles) {
    result[role] = roleHex(scheme, role);
  }
  return result;
}

const fixtures = { source: '@material/material-color-utilities', schemes: [], palettes: [] };

for (const seed of seeds) {
  const hct = Hct.fromInt(argbFromHex(seed));

  fixtures.palettes.push({
    seed,
    hue: Number(hct.hue.toFixed(4)),
    chroma: Number(hct.chroma.toFixed(4)),
    tone: Number(hct.tone.toFixed(4)),
    // The tonal spot palettes that the scheme is built from.
    primary: tones.map((tone) => hexFromArgb(TonalPalette.fromHueAndChroma(hct.hue, 36.0).tone(tone))),
    secondary: tones.map((tone) => hexFromArgb(TonalPalette.fromHueAndChroma(hct.hue, 16.0).tone(tone))),
    tertiary: tones.map((tone) => hexFromArgb(TonalPalette.fromHueAndChroma(hct.hue + 60.0, 24.0).tone(tone))),
    neutral: tones.map((tone) => hexFromArgb(TonalPalette.fromHueAndChroma(hct.hue, 6.0).tone(tone))),
    neutralVariant: tones.map((tone) => hexFromArgb(TonalPalette.fromHueAndChroma(hct.hue, 8.0).tone(tone))),
    error: tones.map((tone) => hexFromArgb(TonalPalette.fromHueAndChroma(25.0, 84.0).tone(tone))),
    tones,
  });

  for (const [schemeName, makeScheme] of [
    ['tonalSpot', (isDark) => new SchemeTonalSpot(hct, isDark, 0.0)],
    ['expressive', (isDark) => new SchemeExpressive(hct, isDark, 0.0)],
  ]) {
    for (const isDark of [false, true]) {
      fixtures.schemes.push({
        seed,
        scheme: schemeName,
        dark: isDark,
        contrast: 0,
        roles: schemeFixture(makeScheme(isDark)),
      });
    }
  }
}

mkdirSync(outputDirectory, { recursive: true });
const path = join(outputDirectory, 'material-color-fixtures.json');
writeFileSync(path, `${JSON.stringify(fixtures, null, 2)}\n`);
console.log(`wrote ${path}`);
console.log(`  ${fixtures.palettes.length} palettes, ${fixtures.schemes.length} schemes`);

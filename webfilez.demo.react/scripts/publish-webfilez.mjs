// Publishes WebFilez.CustomElements, whose output vite.config.js serves at /_framework and /_content.
// Skips the publish when output already exists, unless run with --force (npm run webfilez).
import { execFileSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { webFilezOutput, webFilezProject, webFilezPublishDir } from './paths.mjs';

const force = process.argv.includes('--force');

if (!force && existsSync(webFilezOutput)) {
    console.log('WebFilez custom element already published. Run "npm run webfilez" to republish.');
    process.exit(0);
}

console.log('Publishing the WebFilez custom element...');
execFileSync('dotnet', ['publish', webFilezProject, '-c', 'Release', '-o', webFilezPublishDir, '--nologo'], { stdio: 'inherit' });

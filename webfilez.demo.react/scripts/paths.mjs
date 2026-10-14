import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

export const webFilezProject = path.join(root, 'WebFilez.CustomElements', 'WebFilez.CustomElements.csproj');
export const webFilezPublishDir = path.join(root, 'WebFilez.CustomElements', 'bin', 'publish');
export const webFilezOutput = path.join(webFilezPublishDir, 'wwwroot');

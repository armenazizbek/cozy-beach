// Builds the Unity game and lays it out in release/unity-win-unpacked, ready to run or to upload to Steam.
//
//   npm run dist:unity          the release build (IL2CPP: slower to make, the one that ships)
//   npm run dist:unity:quick    a quick Mono build for trying a change (minutes faster to make, slower to run)
//
// The Unity editor must be closed, and so must the game (a running game locks its own files).
// Unity is found from the version in unity/ProjectSettings/ProjectVersion.txt; set UNITY_EDITOR to the full path of
// Unity.exe if it is installed somewhere unusual.
import { spawnSync } from 'node:child_process'
import fs from 'node:fs'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const unityDir = path.join(root, 'unity')
const built = path.join(unityDir, 'Builds', 'Windows')
const out = path.join(root, 'release', 'unity-win-unpacked')
const exe = 'CozyTreasureHunter.exe'
const quick = process.argv.includes('--quick')

function fail(message) {
  console.error(`\n${message}\n`)
  process.exit(1)
}

function findUnity() {
  const versionFile = fs.readFileSync(path.join(unityDir, 'ProjectSettings', 'ProjectVersion.txt'), 'utf8')
  const version = /m_EditorVersion:\s*(\S+)/.exec(versionFile)?.[1]
  const places = [
    process.env.UNITY_EDITOR,
    version && path.join(os.homedir(), 'Unity', 'Hub', 'Editor', version, 'Editor', 'Unity.exe'),
    version && path.join(process.env.ProgramFiles ?? 'C:\\Program Files', 'Unity', 'Hub', 'Editor', version, 'Editor', 'Unity.exe'),
  ].filter(Boolean)
  const found = places.find((p) => fs.existsSync(p))
  if (!found) {
    fail(`Unity ${version ?? ''} was not found. Looked in:\n  ${places.join('\n  ')}\nInstall that version with Unity Hub, or set UNITY_EDITOR to the full path of Unity.exe.`)
  }
  return found
}

/** True while something holds the file open (the editor holds its lock file, a running game its own exe). */
function inUse(file) {
  if (!fs.existsSync(file)) return false
  try {
    fs.closeSync(fs.openSync(file, 'r+'))
    return false
  } catch {
    return true
  }
}

/** Build pieces that must not ship: debug symbols, about 1.2 GB. */
const notForShipping = (name) => name.endsWith('_DoNotShip') || name.endsWith('_ButDontShipItWithYourGame')

/** What a Unity build puts in its folder, Mono or IL2CPP. Anything else in the output folder is yours and is left alone. */
const isBuildPiece = (name) =>
  name === exe ||
  name.endsWith('_Data') ||
  ['D3D12', 'MonoBleedingEdge', 'GameAssembly.dll', 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'baselib.dll', 'ThirdPartyNotices.txt'].includes(name)

if (process.platform !== 'win32') fail('This makes the Windows build, so it has to run on Windows.')
const unity = findUnity()
if (inUse(path.join(unityDir, 'Temp', 'UnityLockfile'))) fail('The Unity editor has this project open. Close it and run the command again.')
if (inUse(path.join(built, exe)) || inUse(path.join(out, exe))) fail('The game is running. Close it and run the command again.')

const method = quick ? 'BuildScript.BuildWindowsMono' : 'BuildScript.BuildWindows'
const log = path.join(unityDir, quick ? 'build_mono.log' : 'build.log')
console.log(`Building with ${path.basename(path.dirname(path.dirname(unity)))} (${quick ? 'quick Mono build' : 'release build, IL2CPP'}).`)
console.log(`This takes ${quick ? 'a minute or two' : 'several minutes'}; Unity writes its progress to ${path.relative(root, log)}`)
const started = Date.now()
const run = spawnSync(unity, ['-batchmode', '-quit', '-projectPath', unityDir, '-executeMethod', method, '-logFile', log], { stdio: 'inherit' })
const text = fs.existsSync(log) ? fs.readFileSync(log, 'utf8') : ''
const ok = run.status === 0 && fs.existsSync(path.join(built, exe)) && /Windows build succeeded \((Mono|IL2CPP)\)/.test(text)
if (!ok) {
  const why = [...new Set(text.split(/\r?\n/).filter((l) => /error CS\d+|Windows build failed|Scripts have compiler errors|another Unity instance/i.test(l)))].slice(0, 25)
  fail(`The Unity build failed${run.error ? `: ${run.error.message}` : ''}.\n${why.join('\n') || `See ${log}`}`)
}
const backend = /Windows build succeeded \((Mono|IL2CPP)\)/.exec(text)[1]
if (!quick && backend !== 'IL2CPP') {
  console.warn('Note: IL2CPP is not installed for this Unity, so this is a Mono build. Add "Windows Build Support (IL2CPP)" in Unity Hub for the release build.')
}

// lay it out for running or uploading: the last build's pieces go, the new ones come in without the debug symbols
fs.mkdirSync(out, { recursive: true })
try {
  for (const name of fs.readdirSync(out)) {
    if (isBuildPiece(name)) fs.rmSync(path.join(out, name), { recursive: true, force: true })
  }
  for (const name of fs.readdirSync(built)) {
    if (notForShipping(name)) continue
    fs.cpSync(path.join(built, name), path.join(out, name), { recursive: true })
  }
} catch (e) {
  fail(`The build worked, but it could not be copied to ${out}:\n${e.message}\nIf the game is running from that folder, close it and run the command again.`)
}

const seconds = Math.round((Date.now() - started) / 1000)
console.log(`\nDone in ${Math.floor(seconds / 60)} min ${seconds % 60} s (${backend}). The game is ready to test:`)
console.log(`  ${path.join(out, exe)}`)

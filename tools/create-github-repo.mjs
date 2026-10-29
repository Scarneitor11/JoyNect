import { execSync } from "node:child_process";
import { request } from "node:http";
import { request as httpsRequest } from "node:https";
import { URL } from "node:url";

const GITHUB_API = "https://api.github.com";
const REPO_NAME = "JoyNect";
const PRIVATE = false;

function isHttps(url) {
  return url.startsWith("https:");
}

function makeRequest(url, options) {
  const parsed = new URL(url);
  const transport = parsed.protocol === "https:" ? httpsRequest : request;
  return new Promise((resolve, reject) => {
    const req = transport(
      {
        hostname: parsed.hostname,
        port: parsed.port || (parsed.protocol === "https:" ? 443 : 80),
        path: parsed.pathname + parsed.search,
        method: options.method || "GET",
        headers: options.headers,
      },
      (res) => {
        let data = "";
        res.on("data", (c) => (data += c));
        res.on("end", () => resolve({ status: res.statusCode, body: data }));
      }
    );
    req.on("error", reject);
    if (options.body) {
      req.write(options.body);
    }
    req.end();
  });
}

function parseGitCredentialOutput(raw) {
  const kv = {};
  for (const line of raw.split(/\r?\n/)) {
    const idx = line.indexOf("=");
    if (idx < 0) continue;
    kv[line.slice(0, idx)] = line.slice(idx + 1);
  }
  return kv;
}

function findGitCredentialManager() {
  try {
    const out = execSync(
      'where git-credential-manager 2>nul || where git-credential-manager.exe 2>nul',
      { encoding: "utf8", windowsHide: true }
    )
      .trim()
      .split(/\r?\n/)
      .filter(Boolean);
    return out[0] || null;
  } catch {
    return null;
  }
}

function getStoredGitHubToken() {
  const askpass = findGitCredentialManager();
  if (!askpass) return null;
  try {
    const raw = execSync(`"${askpass}" get`, {
      input: "protocol=https\nhost=github.com\n",
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
      maxBuffer: 10 * 1024 * 1024,
    });
    const kv = parseGitCredentialOutput(raw);
    return kv.token || kv.password || null;
  } catch (err) {
    return null;
  }
}

async function main() {
  const token = getStoredGitHubToken();
  if (!token) {
    console.error("No stored GitHub token could be read from git credential manager.");
    console.error("I found the credential manager at:", findGitCredentialManager() || "none");
    console.error("This run needs an interactive auth step, so I cannot create the repo headless.");
    process.exit(2);
  }

  console.log("Using stored GitHub credential for api.github.com.");
  const headers = {
    Accept: "application/vnd.github+json",
    "X-GitHub-Api-Version": "2022-11-28",
    "Content-Type": "application/json",
    "User-Agent": "JoyNectRepoCreate/1.0 (git; windows)",
    Authorization: `Bearer ${token}`,
  };

  const body = JSON.stringify({ name: REPO_NAME, private: PRIVATE, auto_init: false });

  console.log("Creating repository:", REPO_NAME, PRIVATE ? "(private)" : "(public)");
  const result = await makeRequest(`${GITHUB_API}/user/repos`, {
    method: "POST",
    headers,
    body,
  });

  if (result.status >= 400) {
    console.error("GitHub API responded with:", result.status);
    console.error(result.body);
    process.exit(1);
  }

  const repo = JSON.parse(result.body);
  console.log("Created repository:", repo.full_name);
  console.log("clone url:", repo.clone_url);
  console.log("ssh url:", repo.ssh_url);
  console.log("html url:", repo.html_url);
}

main().then(
  () => process.exit(0),
  (err) => {
    console.error(err);
    process.exit(1);
  }
);

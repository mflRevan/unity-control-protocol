"use strict";

const https = require("https");
const http = require("http");
const fs = require("fs");
const path = require("path");

const pkg = require("../package.json");
const version = pkg.version;

const PLATFORM_MAP = {
  "darwin-x64": "ucp-darwin-x64",
  "darwin-arm64": "ucp-darwin-arm64",
  "linux-x64": "ucp-linux-x64",
  "win32-x64": "ucp-win32-x64.exe",
};

const key = `${process.platform}-${process.arch}`;
const artifact = PLATFORM_MAP[key];

if (!artifact) {
  console.error(`Unsupported platform: ${key}`);
  console.error(`Supported: ${Object.keys(PLATFORM_MAP).join(", ")}`);
  process.exit(1);
}

const nativeDir = path.join(__dirname, "..", "native");
fs.mkdirSync(nativeDir, { recursive: true });

const ext = process.platform === "win32" ? ".exe" : "";
const dest = path.join(nativeDir, `ucp${ext}`);

// Skip download if binary already exists (local dev / rebuild)
if (fs.existsSync(dest)) {
  console.log("ucp binary already present, skipping download.");
  process.exit(0);
}

// Allow local binary override via env var (for local testing)
if (process.env.UCP_LOCAL_BINARY) {
  const src = path.resolve(process.env.UCP_LOCAL_BINARY);
  if (!fs.existsSync(src)) {
    console.error(`UCP_LOCAL_BINARY set but file not found: ${src}`);
    process.exit(1);
  }
  fs.copyFileSync(src, dest);
  if (process.platform !== "win32") fs.chmodSync(dest, 0o755);
  console.log(`Copied local binary from ${src}`);
  process.exit(0);
}

const url = `https://github.com/mflRevan/unity-control-protocol/releases/download/v${version}/${artifact}`;
console.log(`Downloading ucp v${version} for ${key}...`);
console.log(`  ${url}`);

download(url, dest, (err) => {
  if (err) {
    console.error(`Failed to download ucp: ${err.message}`);
    console.error(
      "You can build from source instead: cd cli && cargo build --release"
    );
    process.exit(1);
  }
  if (process.platform !== "win32") {
    fs.chmodSync(dest, 0o755);
  }
  console.log("ucp installed successfully.");
});

function download(url, dest, cb, redirects) {
  if (redirects === undefined) redirects = 0;
  if (redirects > 5) return cb(new Error("Too many redirects"));

  var get = url.startsWith("https") ? https.get : http.get;
  get(url, function (res) {
    if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
      return download(res.headers.location, dest, cb, redirects + 1);
    }
    if (res.statusCode !== 200) {
      res.resume();
      return cb(new Error(`HTTP ${res.statusCode} from ${url}`));
    }
    var file = fs.createWriteStream(dest);
    res.pipe(file);
    file.on("finish", function () {
      file.close(cb);
    });
    file.on("error", function (err) {
      fs.unlink(dest, function () {});
      cb(err);
    });
  }).on("error", cb);
}

"""
Resolve SQLite paths for the dashboard: env vars, optional .env, then defaults
next to this file.

Env (optional):
  BOTF_VOLATILITY_DB — path to volatility.db
  BOTF_DASHBOARD_DB  — path to dashboard.db
  BOTF_BARS_DB       — path to bars.db

Also loads web/dashboard/.env on import (KEY=VALUE lines; uses setdefault).
"""
from __future__ import annotations

import os
from pathlib import Path

_DASH_DIR = Path(__file__).resolve().parent


def _load_dotenv() -> None:
    p = _DASH_DIR / ".env"
    if not p.is_file():
        return
    try:
        text = p.read_text(encoding="utf-8")
    except OSError:
        return
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, _, val = line.partition("=")
        key, val = key.strip(), val.strip().strip('"').strip("'")
        if key:
            os.environ.setdefault(key, val)


_load_dotenv()


def _path_from_env(key: str, default_filename: str) -> Path:
    override = (os.environ.get(key) or "").strip()
    if override:
        return Path(os.path.normpath(os.path.expanduser(override))).resolve()
    return _DASH_DIR / default_filename


def volatility_db_path() -> Path:
    return _path_from_env("BOTF_VOLATILITY_DB", "volatility.db")


def dashboard_db_path() -> Path:
    return _path_from_env("BOTF_DASHBOARD_DB", "dashboard.db")


def bars_db_path() -> Path:
    return _path_from_env("BOTF_BARS_DB", "bars.db")

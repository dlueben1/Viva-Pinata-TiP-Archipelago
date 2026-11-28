"""One-shot ReTiP launcher, imported only when Launch TiP runs."""

import hashlib
import json
import os
import re
import subprocess
from pathlib import Path, PureWindowsPath
from tempfile import NamedTemporaryFile
from urllib.parse import urlsplit

from Utils import messagebox, open_filename, user_path
from kvui import (
    ThemedApp,
)  # Archipelago configures frozen resources before importing Kivy.
from kivy.core.window import Window
from kivy.metrics import dp
from kivy.uix.boxlayout import BoxLayout
from kivy.uix.button import Button
from kivy.uix.label import Label
from kivy.uix.textinput import TextInput


def read_patch(path):
    data = json.loads(Path(path).read_bytes())
    fields = (
        {key.lower(): value for key, value in data.items()}
        if isinstance(data, dict)
        else {}
    )
    version = fields.get("version")
    if type(version) is not int or version != 1:
        raise ValueError("Select a version 1 .aptip patch.")
    seed, slot = fields.get("seed"), fields.get("slot")
    for key, value in (("seed", seed), ("slot", slot)):
        if not isinstance(value, str) or not value.strip() or "\0" in value:
            raise ValueError(f"The patch must contain a valid {key}.")
        value.encode(
            "utf-8"
        )  # Reject unpaired surrogates before displaying or launching.
    return seed, slot


def valid_connection(server, port):
    server, port = server.strip(), port.strip()
    if not re.fullmatch(r"\+?[0-9]+", port) or re.search(r"[\s\x00-\x1f\\]", server):
        return False
    try:
        address = urlsplit(server if "://" in server else "ws://" + server)
        host = address.hostname or ""
        if address.scheme not in {"ws", "wss", "http", "https"}:
            return False
        _ = address.port  # urlsplit validates an embedded port on first access.
        if ":" not in host:  # urlsplit already validated bracketed IPv6.
            domain = host.removesuffix(".").encode("idna").decode("ascii")
            if not re.fullmatch(r"[\w-]+(?:\.[\w-]+)*", domain):
                return False
        return 1 <= int(port) <= 65535
    except ValueError:
        return False


def session_directory(root, seed, slot):
    sessions = root / "sessions"
    if sessions.resolve() != sessions:
        raise ValueError("The sessions folder points to another directory.")
    sessions.mkdir(parents=True, exist_ok=True)
    name = f"{seed}_{slot}"
    clean = re.sub(r'[<>:"/\\|?*\x00-\x1f~]', "_", name).rstrip(" .")
    # Keep ordinary WPF names. Reserve "~" for names that would alias on Windows.
    if (
        name == clean
        and "_" not in seed
        and len(name.encode("utf-8")) <= 180
        and not PureWindowsPath(name).is_reserved()
    ):
        path = sessions / name
        if path.resolve() != path:
            raise ValueError("The session folder points to another directory.")
        path.mkdir(exist_ok=True)
        if (
            path.resolve().name == name
        ):  # Windows may have matched another slot's casing.
            return path

    digest = hashlib.sha256(json.dumps([seed, slot]).encode("ascii")).hexdigest()[:20]
    path = sessions / f"~{digest}_{clean[:60].rstrip(' .')}"
    if path.resolve() != path:
        raise ValueError("The session folder points to another directory.")
    path.mkdir(exist_ok=True)
    return path


def is_game(path):
    exe = Path(path)
    return exe.is_absolute() and exe.name.lower() == "retip.exe" and exe.is_file()


class TiPLauncher(ThemedApp):
    title = "TiP Archipelago Launcher"
    patch = None

    def build(self):
        self.data_root = Path(user_path("VivaAP")).resolve()
        self.ini = self.data_root / "launcher.ini"
        self.set_colors()
        Window.minimum_width, Window.minimum_height = dp(460), dp(470)
        Window.size = (dp(550), dp(490))
        Window.clearcolor = (0.12, 0.16, 0.21, 1)
        form = BoxLayout(orientation="vertical", padding=dp(20), spacing=dp(6))
        form.add_widget(
            Label(
                text="[color=ff966e]TiP[/color] [color=94dec6]Archipelago[/color] "
                "[color=a8bcff]Launcher[/color]",
                markup=True,
                bold=True,
                font_size="24sp",
                size_hint_y=None,
                height=dp(46),
            )
        )

        def caption(parent, text):
            label = Label(
                text=text,
                halign="left",
                valign="middle",
                size_hint_y=None,
                height=dp(22),
            )
            label.bind(size=label.setter("text_size"))
            parent.add_widget(label)
            return label

        def field(parent, title, **options):
            caption(parent, title)
            widget = TextInput(multiline=False, write_tab=False, **options)
            parent.add_widget(widget)
            return widget

        def picker(title, browse):
            caption(form, title)
            row = BoxLayout(spacing=dp(8))
            path = TextInput(readonly=True, multiline=False, write_tab=False)
            row.add_widget(path)
            row.add_widget(
                Button(
                    text="Browse…", size_hint_x=None, width=dp(98), on_release=browse
                )
            )
            form.add_widget(row)
            return path

        connection = BoxLayout(spacing=dp(8), size_hint_y=None, height=dp(59))
        server_column = BoxLayout(orientation="vertical")
        port_column = BoxLayout(orientation="vertical", size_hint_x=None, width=dp(98))
        self.server = field(server_column, "Server URL / IP")
        self.port = field(port_column, "Port", text="38281")
        connection.add_widget(server_column)
        connection.add_widget(port_column)
        form.add_widget(connection)
        self.password = field(form, "Password (optional)", password=True)
        self.patch_path = picker("Patch file (.aptip)", self.browse_patch)
        self.details = caption(form, "No patch selected")
        self.details.shorten = True
        self.game_path = picker("ReTiP executable", self.browse_game)
        try:
            for line in self.ini.read_text(encoding="utf-8-sig").splitlines():
                key, _, path = line.partition("=")
                if key.strip().lower() == "gamepath" and is_game(path.strip()):
                    self.game_path.text = path.strip()
                    break
        except (OSError, ValueError):
            pass  # A missing or unreadable preference leaves the picker available.

        self.start = Button(
            text="Start",
            bold=True,
            disabled=True,
            size_hint_y=None,
            height=dp(42),
            on_release=self.start_game,
        )
        form.add_widget(self.start)
        self.server.bind(text=self.refresh)
        self.port.bind(text=self.refresh)
        return form

    def refresh(self, *_args):
        self.start.disabled = not (
            self.patch
            and Path(self.patch_path.text).is_file()
            and is_game(self.game_path.text)
            and valid_connection(self.server.text, self.port.text)
        )

    def browse_patch(self, *_args):
        path = open_filename(
            "Select Archipelago Patch", (("TiP Archipelago Patch", (".aptip",)),)
        )
        if not path:
            return
        try:
            self.patch = read_patch(path)
            self.patch_path.text = path
            self.details.text = f"Seed: {self.patch[0]}    |    Slot: {self.patch[1]}"
        except (OSError, ValueError, RecursionError) as exc:
            self.patch = None
            self.patch_path.text = ""
            self.details.text = "No valid patch selected"
            messagebox("Unable to load patch", str(exc), error=True)
        self.refresh()

    def browse_game(self, *_args):
        path = open_filename("Select ReTiP Executable", (("ReTiP", ("retip.exe",)),))
        if not path:
            return
        if not is_game(path):
            messagebox(
                "Invalid game location", "Select an existing retip.exe.", error=True
            )
            return
        self.game_path.text = path
        self.refresh()
        try:
            self.data_root.mkdir(parents=True, exist_ok=True)
            pending = NamedTemporaryFile(
                mode="w", encoding="utf-8", dir=self.data_root, delete=False
            )
            try:
                with pending:
                    pending.write(f"GamePath={path}\n")
                os.replace(pending.name, self.ini)
            finally:
                Path(pending.name).unlink(missing_ok=True)
        except OSError as exc:
            messagebox("Unable to remember game location", str(exc), error=True)

    def start_game(self, *_args):
        self.refresh()
        if self.start.disabled:
            messagebox(
                "Unable to start",
                "Check the connection and select both files again.",
                error=True,
            )
            return
        seed, slot = self.patch
        exe = Path(self.game_path.text)
        try:
            directory = session_directory(self.data_root, seed, slot)
            env = dict(
                os.environ,
                AP_LAUNCHED="1",
                REX_USER_DATA_ROOT=str(directory),
                AP_SERVER=self.server.text.strip(),
                AP_PASSWORD=self.password.text,
                AP_PORT=self.port.text.strip(),
                AP_SLOT=slot,
                AP_SEED=seed,
            )
            subprocess.Popen([str(exe)], cwd=exe.parent, env=env)
        except (OSError, ValueError) as exc:
            messagebox("Unable to launch ReTiP", str(exc), error=True)
            return
        self.stop()

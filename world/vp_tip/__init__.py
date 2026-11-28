from worlds.LauncherComponents import Component, Type, components, launch

from .world import TIPWorld


def _run_launcher():
    from .launcher import TiPLauncher

    TiPLauncher().run()


def launch_tip(*_args):
    launch(_run_launcher, name="Launch TiP")


components.append(Component("Launch TiP", func=launch_tip, component_type=Type.TOOL,
                            description="Launch ReTiP with your Archipelago patch and connection settings."))

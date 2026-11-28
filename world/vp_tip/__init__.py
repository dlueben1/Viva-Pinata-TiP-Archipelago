from worlds.LauncherComponents import Component, Type, components, icon_paths, launch

from .world import TIPWorld


def _run_launcher():
    from .launcher import TiPLauncher

    TiPLauncher().run()


def launch_tip(*_args):
    launch(_run_launcher, name="Launch TiP")


icon_paths["vp_tip"] = f"ap:{__name__}/VPTIP_AP_Logo_Squared.png"
components.append(Component("Launch TiP", func=launch_tip, component_type=Type.TOOL, icon="vp_tip",
                            description="Launch ReTiP with your Archipelago patch and connection settings."))

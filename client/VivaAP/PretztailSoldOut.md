# Pretztail House Sold Out experiment

Use the staged `dist\pretztail-offline\mod` folder as `mods\VivaAP` in a disposable ReTiP installation. The experiment is off by default and requires the verified game and ReXFabric build. Use an ordinary local garden with access to Willy Builder, sufficient coins, and no purchased Pretztail House. To cover residency and its award, start before Pretztail first becomes resident. Use a fresh profile or a disposable copy of a suitable profile and garden.

From that installation's directory, create the dedicated data root. If you are using a disposable save copy, populate the root before launching. Leave this PowerShell window available for the arm commands.

```powershell
$root = Join-Path ([IO.Path]::GetTempPath()) 'VivaAP-Pretztail-Test'
New-Item -ItemType Directory -Path $root -Force | Out-Null
$arm = Join-Path $root 'pretztail-sold-out.arm'
```

```powershell
$env:REX_USER_DATA_ROOT = $root
$env:VIVAAP_PRETZTAIL_EXPERIMENT = '1'
Start-Process -FilePath (Join-Path $PWD 'retip.exe') -WorkingDirectory $PWD -ArgumentList "--user_data_root `"$root`""
```

Confirm that ReTiP's startup user-data root is `$root` and the log says both Pretztail hooks are registered. Stop if either check fails. This message confirms installation. The later reel and placement messages confirm that each hook qualified a local call.

Create the arm file to enable the restriction. Wait for `Pretztail restriction armed` in the log before opening Willy or placing a held preview.

```powershell
New-Item -ItemType File -Path $arm -Force | Out-Null
```

Remove the arm file to disable the restriction. Wait for `Pretztail restriction off` and rebuild the reel through normal navigation when you need to see current availability.

```powershell
Remove-Item -LiteralPath $arm
```

## Disposable garden run

1. Arm the restriction before residency. Let Pretztail become resident and verify normal native progression and the existing `Award earned` log. To confirm server receipt, run the client's existing local AP setup at `127.0.0.1:38281` with the disposable `Pinata` slot and check that the server marks the corresponding location complete.
2. Open Willy and navigate to the ordinary house reel. Verify that Pretztail House appears with the game's Sold Out art and the log says `Pretztail reel verified`. Attempt to select it and verify the game refuses selection without putting a preview in the cursor.
3. With the restriction still armed, buy and finish an unrelated house you can afford. Verify the normal debit and construction. Save the garden and exit ReTiP.
4. Keep the arm file and relaunch with the same command and root. Load the saved garden, reopen Willy, and confirm that Pretztail House again appears Sold Out and refuses selection. A new process should log a fresh reel verification.
5. Disarm, close and reopen Willy or exit and re-enter the house category, and confirm a fresh reel shows the ordinary Pretztail House icon and allows selection. Select it and hold the preview over a valid, affordable location. Record the balance and any existing construction. Keep the held preview in place.
6. Re-arm from PowerShell. After the log acknowledges the arm, attempt placement from that same preview. Require `Pretztail placement verified and denied before purchase`. Verify no coin debit, purchased house, or construction entry. Cancel the preview through normal controls.
7. Disarm again and rebuild the reel. Verify native availability and, if the garden still meets the normal requirements, place Pretztail House and confirm the ordinary debit and construction. The bridge never writes availability `1`.

Stop the run if there is a reel or placement qualification warning. That native call proceeded with the game's behavior, so the run cannot count as an enforcement pass. To stop between runs, remove the arm file and clear `VIVAAP_PRETZTAIL_EXPERIMENT` from the launch shell before restarting.

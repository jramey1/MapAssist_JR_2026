$ErrorActionPreference = 'Stop'

function Read-Text([string]$Path) {
    return [System.IO.File]::ReadAllText((Join-Path $env:GITHUB_WORKSPACE $Path))
}

function Write-Text([string]$Path, [string]$Content) {
    $fullPath = Join-Path $env:GITHUB_WORKSPACE $Path
    [System.IO.File]::WriteAllText($fullPath, $Content, (New-Object System.Text.UTF8Encoding($false)))
}

$unitAnyPath = 'Types/UnitAny.cs'
$unitAny = Read-Text $unitAnyPath
$unitAnyPattern = '(?s)        public string GetInfo\(\)\s*\{.*?        \}\s*        public override string ToString\(\)'
$unitAnyReplacement = @'
        public virtual string GetInfo()
        {
            return "Name=" + HashString +
                   " UnitId=" + UnitId +
                   " UnitType=" + UnitType +
                   " TxtFileNo=" + TxtFileNo +
                   " Mode=" + Struct.Mode +
                   " Area=" + Area +
                   " X=" + X +
                   " Y=" + Y +
                   " PtrUnit=0x" + PtrUnit.ToInt64().ToString("X") +
                   " pUnitData=0x" + Struct.pUnitData.ToInt64().ToString("X") +
                   " pPath=0x" + Struct.pPath.ToInt64().ToString("X") +
                   " pStatsListEx=0x" + Struct.pStatsListEx.ToInt64().ToString("X") +
                   " pInventory=0x" + Struct.pInventory.ToInt64().ToString("X") +
                   " pListNext=0x" + Struct.pListNext.ToInt64().ToString("X") +
                   " pRoomNext=0x" + Struct.pRoomNext.ToInt64().ToString("X") +
                   " IsValidPointer=" + IsValidPointer +
                   " IsValidUnit=" + IsValidUnit +
                   " IsCached=" + IsCached +
                   " IsHovered=" + IsHovered +
                   " FoundTime=" + FoundTime.ToString("O");
        }

        public override string ToString()
'@
$updatedUnitAny = [regex]::Replace($unitAny, $unitAnyPattern, $unitAnyReplacement, 1)
if ($updatedUnitAny -eq $unitAny) { throw 'UnitAny.GetInfo replacement did not match.' }
Write-Text $unitAnyPath $updatedUnitAny

$playerPath = 'Types/UnitPlayer.cs'
$player = Read-Text $playerPath
if ($player -notmatch 'public override string GetInfo\(\)') {
    $playerMarker = '        public override string HashString => Name + "/" + Position.X + "/" + Position.Y;'
    $playerOverride = @'
        public override string GetInfo()
        {
            return base.GetInfo() +
                   " PlayerName=" + (Name ?? "<null>") +
                   " PlayerClass=" + Struct.playerClass +
                   " Level=" + (Stats == null ? 0 : Level) +
                   " Experience=" + (Stats == null ? 0 : Experience) +
                   " Life=" + (Stats == null ? 0 : Life) +
                   " MaxLife=" + (Stats == null ? 0 : MaxLife) +
                   " Mana=" + (Stats == null ? 0 : Mana) +
                   " MaxMana=" + (Stats == null ? 0 : MaxMana) +
                   " HealthPercentage=" + (Stats == null ? 0 : HealthPercentage) +
                   " LifePercentage=" + (Stats == null ? 0 : LifePercentage) +
                   " ManaPercentage=" + (Stats == null ? 0 : ManaPercentage) +
                   " LevelProgress=" + (Stats == null ? 0 : LevelProgress) +
                   " BeltSize=" + BeltSize +
                   " WearingItemCount=" + (WearingItems == null ? 0 : WearingItems.Length) +
                   " InitSeedHash=" + InitSeedHash +
                   " EndSeedHash=" + EndSeedHash;
        }

'@
    if (-not $player.Contains($playerMarker)) { throw 'UnitPlayer HashString marker not found.' }
    $player = $player.Replace($playerMarker, $playerOverride + $playerMarker)
    Write-Text $playerPath $player
}

$itemPath = 'Types/UnitItem.cs'
$item = Read-Text $itemPath
if ($item -notmatch 'public override string GetInfo\(\)') {
    $itemMarker = '        public override string HashString => Item + "/" + Position.X + "/" + Position.Y;'
    $itemOverride = @'
        public override string GetInfo()
        {
            return base.GetInfo() +
                   " Item=" + Item +
                   " ItemBaseName=" + (ItemBaseName ?? "<null>").Replace("\r", "\\r").Replace("\n", "\\n") +
                   " ItemMode=" + ItemMode +
                   " ItemModeMapped=" + ItemModeMapped +
                   " ItemQuality=" + ItemData.ItemQuality +
                   " MappedItemQuality=" + (MappedItemQuality.HasValue ? MappedItemQuality.Value.ToString() : "<null>") +
                   " ItemLevel=" + ItemData.ilvl +
                   " ItemFlags=" + ItemData.ItemFlags +
                   " OwnerUnitId=" + ItemData.dwOwnerID +
                   " InvPage=" + ItemData.InvPage +
                   " BodyLoc=" + ItemData.BodyLoc +
                   " StashTab=" + StashTab +
                   " VendorOwner=" + VendorOwner +
                   " IsValidItem=" + IsValidItem +
                   " IsPlayerOwned=" + IsPlayerOwned +
                   " IsEthereal=" + IsEthereal +
                   " IsIdentified=" + IsIdentified +
                   " IsRuneWord=" + IsRuneWord +
                   " IsDropped=" + IsDropped +
                   " IsAnyPlayerHolding=" + IsAnyPlayerHolding;
        }

'@
    if (-not $item.Contains($itemMarker)) { throw 'UnitItem HashString marker not found.' }
    $item = $item.Replace($itemMarker, $itemOverride + $itemMarker)
    Write-Text $itemPath $item
}

$maExportPath = 'MAExport.cs'
$maExport = Read-Text $maExportPath
$reportsPattern = '(?s)        #region reports.*?        #endregion reports'
$reportsReplacement = @'
        #region reports
        public string GetAllUnitsReport()
        {
            Update();

            StringBuilder sb = new StringBuilder();

            foreach (UnitAny unit in CurrentUnitList)
            {
                sb.AppendLine(unit.GetInfo());
            }

            return sb.ToString();
        }

        public string GetAllEnemiesReport()
        {
            Update();

            StringBuilder sb = new StringBuilder();

            foreach (UnitMonster unitMonster in getEnemies())
            {
                sb.AppendLine(unitMonster.GetInfo());
            }

            return sb.ToString();
        }

        public string GetAllItemsReport()
        {
            Update();

            StringBuilder sb = new StringBuilder();
            int count = 0;

            foreach (UnitItem unitItem in CurrentGameData.AllItems)
            {
                ++count;
                sb.AppendLine(unitItem.GetInfo());
            }

            return "Count: " + count + Environment.NewLine + sb;
        }

        public string GetInventoryItemsReport()
        {
            Update();

            StringBuilder sb = new StringBuilder();
            int count = 0;

            foreach (UnitItem unitItem in getItemsInInventory())
            {
                ++count;
                sb.AppendLine(unitItem.GetInfo());
            }

            return "Count: " + count + Environment.NewLine + sb;
        }

        public string GetFilteredItemsReport()
        {
            Update();

            StringBuilder sb = new StringBuilder();

            foreach (UnitItem unitItem in CurrentGameData.AllItems)
            {
                if (itemMatchesLootFilter(unitItem))
                {
                    sb.AppendLine(unitItem.GetInfo());
                }
            }

            return sb.ToString();
        }

        public string GetGroundItemsReport()
        {
            Update();

            StringBuilder sb = new StringBuilder();

            foreach (UnitItem unitItem in getItemsOnGround())
            {
                sb.AppendLine(unitItem.GetInfo());
            }

            return sb.ToString();
        }

        public string GetFilteredGroundItemsReport()
        {
            Update();

            StringBuilder sb = new StringBuilder();

            foreach (UnitItem unitItem in getItemsOnGroundMatchingLootFilter())
            {
                sb.AppendLine(unitItem.GetInfo());
            }

            return sb.ToString();
        }
        #endregion reports
'@
$updatedMAExport = [regex]::Replace($maExport, $reportsPattern, $reportsReplacement, 1)
if ($updatedMAExport -eq $maExport) { throw 'MAExport reports region replacement did not match.' }
Write-Text $maExportPath $updatedMAExport

# Restore the workflow and remove this temporary patch script so neither appears in the PR diff.
git show HEAD^:.github/workflows/build-on-push.yml | Set-Content -Path (Join-Path $env:GITHUB_WORKSPACE '.github/workflows/build-on-push.yml') -Encoding UTF8
Remove-Item -LiteralPath (Join-Path $env:GITHUB_WORKSPACE 'tools/apply-unit-getinfo-report-patch.ps1')

git config user.name 'github-actions[bot]'
git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
git add Types/UnitAny.cs Types/UnitPlayer.cs Types/UnitItem.cs MAExport.cs .github/workflows/build-on-push.yml tools/apply-unit-getinfo-report-patch.ps1
git commit -m 'Add virtual unit info reports'
git push origin HEAD

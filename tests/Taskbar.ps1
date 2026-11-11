param([string]$Exe,[string]$Demo)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class StageShellProbe {
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
 [DllImport("user32.dll")] public static extern bool GetPhysicalCursorPos(out Point p);
 [DllImport("user32.dll")] public static extern bool SetPhysicalCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPhysicalPoint(Point p);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr c);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h,int i);
}
'@
[void][StageShellProbe]::SetThreadDpiAwarenessContext([IntPtr](-4))
function Items {
 $tray=[StageShellProbe]::FindWindow('Shell_TrayWnd',$null)
 if (!$tray.ToInt64()) {throw 'Explorer taskbar unavailable'}
 $root=[Windows.Automation.AutomationElement]::FromHandle($tray)
 $list=$root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
 foreach($e in $list){
   $v=$e.Current
   if (($v.Name+' '+$v.AutomationId) -match 'pdf[ ._-]?stage' -and $v.ControlType.ProgrammaticName -eq 'ControlType.Button') {
      $e
   }
 }
}
function AwaitItem([bool]$visible) {
 $until=[DateTime]::UtcNow.AddSeconds(7)
 do {
    $items=@(Items)
    if (($items.Count -gt 0) -eq $visible) { return $items }
    Start-Sleep -Milliseconds 150
 } while([DateTime]::UtcNow -lt $until)
 throw "Explorer taskbar: expected PDF Stage item present=$visible (found $($items.Count))"
}
if (@(Items).Count -ne 0) {throw 'Close other PDF Stage windows before this interactive test.'}
$p=Start-Process -FilePath (Resolve-Path -LiteralPath $Exe).Path -ArgumentList ('"'+(Resolve-Path -LiteralPath $Demo).Path+'"') -PassThru
try {
 $p.WaitForInputIdle(6000) | Out-Null
 $item=@(AwaitItem $true)[0]
 $p.Refresh(); $h=$p.MainWindowHandle
 Write-Host ('Opened: '+$item.Current.Name+' | '+$item.Current.AutomationId+' | styles: '+[StageShellProbe]::GetWindowLongPtr($h,-16).ToInt64().ToString('X')+' / '+[StageShellProbe]::GetWindowLongPtr($h,-20).ToInt64().ToString('X'))
 [void][StageShellProbe]::PostMessage($h,0x112,[IntPtr]0xf020,[IntPtr]::Zero)
 Start-Sleep -Milliseconds 450
 if(![StageShellProbe]::IsIconic($h)){throw 'Shell minimize failed'}
 $item=@(AwaitItem $true)[0]
 Write-Host ('Minimized: '+$item.Current.Name)
 Write-Host ('Taskbar button bounds: '+$item.Current.BoundingRectangle+' offscreen='+$item.Current.IsOffscreen+' patterns='+($item.GetSupportedPatterns().ProgrammaticName -join ','))
 $save=New-Object StageShellProbe+Point
 [void][StageShellProbe]::GetPhysicalCursorPos([ref]$save)
 try {
   Add-Type -AssemblyName System.Windows.Forms
   $rect=$item.Current.BoundingRectangle
   $screen=[Windows.Forms.Screen]::FromPoint([Drawing.Point]::new([int]$rect.X,[int]$rect.Y))
   [void][StageShellProbe]::SetPhysicalCursorPos([int]$rect.X,($screen.Bounds.Bottom-1))
   Start-Sleep -Milliseconds 900
   $item=@(AwaitItem $true)[0]
   $rect=$item.Current.BoundingRectangle
   $point=New-Object System.Windows.Point ($rect.X+$rect.Width/2),($rect.Y+$rect.Height/2)
   if($item.Current.IsOffscreen -or $rect.IsEmpty){throw 'Target taskbar button is not on screen'}
   $hit=New-Object StageShellProbe+Point
   $hit.X=[int]$point.X; $hit.Y=[int]$point.Y
   [void][StageShellProbe]::SetPhysicalCursorPos($hit.X,$hit.Y)
   if([StageShellProbe]::GetAncestor([StageShellProbe]::WindowFromPhysicalPoint($hit),2) -ne [StageShellProbe]::FindWindow('Shell_TrayWnd',$null)){throw 'Click point is not in the taskbar'}
   [StageShellProbe]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
   [StageShellProbe]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
   Start-Sleep -Milliseconds 160
 } finally { [void][StageShellProbe]::SetPhysicalCursorPos($save.X,$save.Y) }
 $end=[DateTime]::UtcNow.AddSeconds(4)
 while([StageShellProbe]::IsIconic($h) -and [DateTime]::UtcNow -lt $end){Start-Sleep -Milliseconds 100}
 if([StageShellProbe]::IsIconic($h)){throw 'Invoking the taskbar button did not restore the window'}
 Write-Host 'Restored by the Explorer taskbar button'
} finally {
 $p.Refresh()
 if(!$p.HasExited){[void]$p.CloseMainWindow(); if(!$p.WaitForExit(5000)){throw 'Window did not exit'}}
}
[void](AwaitItem $false)
Write-Host 'Closed: process exited and Explorer button disappeared'

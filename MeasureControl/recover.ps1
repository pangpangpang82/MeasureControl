 = "C:\Users\zululin\AppData\Roaming\Windsurf\User\History"
 = @(
    "GndOcDiscreteOutputCh1TestViewModel.cs",
    "GndOcDiscreteOutputCh2TestViewModel.cs",
    "GndOcDiscreteOutputCh3TestViewModel.cs",
    "GndOcDiscreteOutputCh1TestView.xaml",
    "GndOcDiscreteOutputCh2TestView.xaml",
    "GndOcDiscreteOutputCh3TestView.xaml",
    "A28vOc100mADiscreteOutputCh1TestViewModel.cs",
    "A28vOc100mADiscreteOutputCh2TestViewModel.cs",
    "A28vOc100mADiscreteOutputCh1TestView.xaml",
    "A28vOc100mADiscreteOutputCh2TestView.xaml"
)

foreach ($file in ) {
    Write-Host "Looking for ..."
     = 0
     = ""
    
    Get-ChildItem -Path  -Filter "entries.json" -Recurse | ForEach-Object {
         = Get-Content .FullName -Raw
        if ( -match $file) {
             =  | ConvertFrom-Json
            foreach ($entry in .entries) {
                if ($entry.timestamp -gt ) {
                     = $entry.timestamp
                     = Join-Path .DirectoryName $entry.id
                }
            }
        }
    }
    
    if ( -ne "") {
        Write-Host "Found latest backup:  (Timestamp: )"
    } else {
        Write-Host "No backup found."
    }
}

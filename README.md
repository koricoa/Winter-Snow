# Winter Snow

Снегопад на рабочем столе Windows 11. Снежинки накапливаются у нижнего края экрана, где их подбирает снегоуборщик. Из трубы вылетает собранный снег. Можно выбрать человека со снегоуборщиком или машину с отвалом.

## Как запустить

Скачай **WinterSnow.exe** из [последнего релиза](../../releases/latest) и открой его. В окне нажми **Включить**. Там же можно менять скорость снежинок, включать уборку и смотреть обратный отсчёт до Нового года по местному времени. Чтобы полностью закрыть приложение, выбери **Выход** в меню значка снежинки в области уведомлений Windows.

## Сборка из исходников

На Windows 11 открой PowerShell в папке проекта и выполни:

```powershell
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:winexe /codepage:65001 /out:WinterSnow.exe /win32icon:WinterSnow.ico WinterSnow.cs
```

Команда для запуска встроенного теста:

```powershell
(Start-Process -FilePath .\WinterSnow.exe -ArgumentList '--self-test' -Wait -PassThru).ExitCode
```

Код завершения 0 означает, что тест прошёл.

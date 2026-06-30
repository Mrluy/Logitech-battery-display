# Logitech Battery Display

一个不依赖 Logitech G HUB 的 Windows 托盘小工具，用 HID++ 直接读取罗技 LIGHTSPEED 接收器上的无线鼠标电量。

## 使用

```powershell
dotnet run --project .\LogitechBatteryDisplay
```

双击托盘图标可查看状态，右键托盘图标可刷新或退出。

## 诊断

如果托盘显示未知，可以先运行：

```powershell
dotnet run --project .\LogitechBatteryDisplay -- --probe
```

诊断会列出找到的罗技 HID 接收器、响应的设备 slot、HID++ feature，以及是否读到电量。

## 说明

- 不启动、不连接、不依赖 G HUB。
- 当前优先支持 HID++ 2.0 的 `0x1004 Unified Battery`，并回退到 `0x1000 Battery Status`、`0x1001 Battery Voltage`、`0x1F20 ADC Measurement`。
- 已将 `VID_046D&PID_C547` 识别为 LIGHTSPEED 接收器，适配 G520X/G502X 一类无线鼠标接收器。

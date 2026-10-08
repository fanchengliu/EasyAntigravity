# EasyAG 图标

图标采用已选定的「Antigravity 原标 + 右下角蓝色增强标」方案。保留圆润的拱形轮廓和顶部暖色、底部蓝色的渐变，以小加号区分 EasyAG 增强工具。

`assets/logo.svg` 是可编辑母版。原标轮廓和渐变来自 [Google Antigravity Press Assets](https://antigravity.google/press)，增强标为 EasyAG 的附加设计。图像生成用于方案探索，正式资源由 SVG 导出，避免透明边缘噪点。

## 重新导出

```bash
node scripts/build-icons.cjs
```

需要 Node.js 22 或更高版本，以及本地 Chrome 或 Edge；也可以用 `EASYAG_ICON_BROWSER` 指定浏览器路径。命令无需额外 npm 依赖，更新 PNG、包含 16/20/24/32/40/48/64/128/256 尺寸的 ICO，以及页面内嵌图标。

Tauri 重新构建时使用 `src-tauri/icons/` 下的图标。更新已有的未签名 Windows 便携启动器，可以运行：

```powershell
./scripts/update-windows-icon.ps1 -ExecutablePath ./EasyAntigravity.exe -IconPath ./assets/icon.ico -OutputPath ./EasyAntigravity-new.exe
```

该工具写入独立输出文件，替换原生图标资源，并校验 PE 代码段未发生变化。签名程序需要重新构建和签名。

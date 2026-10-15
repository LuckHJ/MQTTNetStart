# 小橘桌宠

这是一个常驻桌面的 WPF 桌宠。左键拖动桌宠移动位置，右键打开菜单，可选择“拍照上传...”进入摄像头预览、抓拍、保存和上传流程，或打开紧凑的数独小游戏。

上传接口为 `http://localhost:8080/api/updateFaceFeature`，以 multipart/form-data 的 `file` 字段发送 JPEG 图片。
# 1. Short self-introduction for LRS:
你说得对，但是这是LRS-preview？一款拥有Fluent外观的作者有雄心壮志的没做完但是暑假会努力开发并且作者坚信自己会做完的开源免费Windows文件资源浏览器的预览版。
You are right, but is this LRS-preview? A preview version of an open-source, free Windows file resource browser that boasts a Fluent design, is the work of an ambitious author, remains unfinished as of now, yet will be vigorously developed over the summer, and which the author firmly believes will be brought to completion.
# 2. Download && Install *(**Important!**)
1. Download one app installer of our releases.
2. Download the certification of LRS. [Click to download.](https://www.mishui.city/upload/CertForLRS.cer).
3. Install the certification. There are two ways for installing it:
## 2.1 Use Windows Powershell (**Recommended**)
1. Open `powershell`. Press Win + R, type powershell in the popup window, and press Enter.
2. Enter the following code, the parts inside "<>" are something you need to replace according to your actual situation.

`Import-Certificate -FilePath "<Path to the certificate you downloaded>" -CertStoreLocation "Cert:CurrentUserTrustedPeople"`

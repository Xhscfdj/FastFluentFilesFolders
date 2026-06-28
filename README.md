# 1. Self-introduction for LRS:
你说得对，但是这是LRS-preview？一款拥有Fluent外观的作者有雄心壮志的没做完但是暑假会努力开发并且作者坚信自己会做完的开源免费Windows文件资源浏览器的预览版。
You are right, but is this LRS-preview? A preview version of an open-source, free Windows file resource browser that boasts a Fluent design, is the work of an ambitious author, remains unfinished as of now, yet will be vigorously developed over the summer, and which the author firmly believes will be brought to completion.
# 2. Download && Install *(**Important!**)
1. Download one app installer of our releases.
2. Download the certification of LRS. [Click to download.](https://www.mishui.city/upload/CertForLRS.cer).
3. Install the certification. There are two ways for installing it:
## 2.1 Use Windows Powershell (**Recommended**)
2.1.1. Open `powershell`. Press Win + R, type powershell in the popup window, and press Enter.
2.1.2. Enter the following code, the parts inside "<>" are something you need to replace according to your actual situation.

`Import-Certificate -FilePath "<Path to the certificate you downloaded>" -CertStoreLocation "Cert:CurrentUserTrustedPeople"`
## 2.2 Install the certification manually:
2.2.1. Double-click your .cer certificate file.
2.2.2. In the window that opens, click "Install Certificate."
2.2.3. In the "Certificate Import Wizard," select "Current User," then click "Next."
2.2.4. Choose "Place all certificates in the following store," then click "Browse."
2.2.5. Find and select "Trusted People" in the list, and click "OK."
2.2.6. Keep clicking "Next" to finish the import.
2.2.7. Once the import is successful, double-clicking the .msix file again should let it install normally.  
+++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++  
4. Double-click the app installer and then you can install and use it.

# 3. Compiling the source code:
## Something you need:
1. Visual Studio (2022 17.13 or later), I use 2026
2. Windows 11 SDK(10.0.26100.0)
3. .NET 10 SDK
4. Git for Windows
## Clone
1. Open git bash, enter `git clone https://www.github.com/Xhscfdj/LiveRootStorage/`, this will create a local copy of LRS.
## Run the project
1. Open Visual Studio
2. Open LRS.slnx.
3. Press `F5`.

You are ready to go! 🎇

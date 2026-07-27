# UWPX — Windows 10 Mobile build

An XMPP / Jabber client for **Windows 10 Mobile**, based on
[UWPX](https://github.com/UWPX/UWPX-Client).

This repository is a Windows 10 Mobile revival of the UWPX client, built from the
last version of the upstream project that predates WinUI — the point at which the
app was still pure native UWP XAML and could target Windows 10 Mobile.

> **Forked from upstream UWPX/UWPX-Client at commit
> [`af681a7e`](https://github.com/UWPX/UWPX-Client/commit/af681a7eb4197127a7f6b1648771cae3733d298c)**
> — *"Added a Windows Logging Channel for logging"* (2018-10-24).
>
> This is deliberately **the last commit compatible with Windows 10 Mobile**. The
> next upstream commit,
> [`897913aa`](https://github.com/UWPX/UWPX-Client/commit/897913aa08150b194e6befd9bbf0e40343b71973)
> (2018-10-26), added the `Microsoft.UI.Xaml` (Windows UI Library) package. WinUI 2.x
> controls have a runtime floor of Windows 10 build 16299, which is above the highest
> build any Windows 10 Mobile device can run — so every later upstream commit is
> unusable on the phone. Starting from `af681a7e` gives the most feature-complete
> UWPX code that is still WinUI-free and phone-capable.

Chat with all your XMPP contacts.

**UWPX is a secure and Open Source XMPP app.** It implements the E**x**tensible
**M**essaging and **P**resence **P**rotocol ([XMPP](https://xmpp.org/)). It is still
in ALPHA state, so expect occasional crashes and unexpected behavior.

---

## Windows 10 Mobile scope

- **Windows 10 Mobile.** Targets phone builds **1703 (10.0.15063)** and the Fall
  Creators Update mobile line **1709 (last W10M build 10.0.15254)**. For desktop
  Windows 10, use upstream [UWPX](https://github.com/UWPX/UWPX-Client) instead.
- **ARM.** Windows 10 Mobile devices are ARM, so this is built and packaged for ARM.

| Setting | Value |
| --- | --- |
| `TargetPlatformMinVersion` | 10.0.15063.0 (1703) — installs on W10M incl. last build 15254 |
| `TargetPlatformVersion` (app) | 10.0.16299.0 (required so the XAML compiler resolves `MarkupExtension`) |
| Architecture | ARM |
| Toolchain | Visual Studio 2017, C++ platform toolset v141 |

## Table of Contents
1. [Features](#features)
2. [ToDo](#todo)
3. [Installation (build it yourself)](#installation-build-it-yourself)
4. [References](#references)
5. [Credits](#credits)

## Features:
| Name | XEPs |
| ------------- | ------------- |
| Data Forms | [XEP-0004](https://xmpp.org/extensions/xep-0004.html "XEP-0004") |
| Service Discovery | [XEP-0030](https://xmpp.org/extensions/xep-0030.html "XEP-0030") |
| MUC | [XEP-0045](https://xmpp.org/extensions/xep-0045.html "XEP-0045") |
| Bookmarks | [XEP-0048](https://xmpp.org/extensions/xep-0048.html "XEP-0048") |
| Publish-Subscribe | [XEP-0060](https://xmpp.org/extensions/xep-0060.html "XEP-0060") |
| Chat State | [XEP-0085](https://xmpp.org/extensions/xep-0085.html "XEP-0085") |
| Personal Eventing Protocol | [XEP-0163](https://xmpp.org/extensions/xep-0163.html "XEP-0163") |
| Message Delivery Receipts | [XEP-0184](https://xmpp.org/extensions/xep-0184.html "XEP-0184") |
| Direct MUC Invitations | [XEP-0249](https://xmpp.org/extensions/xep-0249.html "XEP-0249") |
| Message Carbons | [XEP-0280](https://xmpp.org/extensions/xep-0280.html "XEP-0280") |
| Message Processing Hints | [XEP-0334](https://xmpp.org/extensions/xep-0334.html "XEP-0334") |

## ToDo:
| Name | XEPs |
| ------------- | ------------- |
| vcard-temp | [XEP-0054](https://xmpp.org/extensions/xep-0054.html "XEP-0054") |
| User Avatar | [XEP-0084](https://xmpp.org/extensions/xep-0084.html "XEP-0084") |
| Jingle | [XEP-0166](https://xmpp.org/extensions/xep-0166.html "XEP-0166") |
| Stream Management | [XEP-0198](https://xmpp.org/extensions/xep-0198.html "XEP-0198") |
| Message Archive Management | [XEP-0313](https://xmpp.org/extensions/xep-0313.html "XEP-0313") |
| Chat Markers | [XEP-0333](https://xmpp.org/extensions/xep-0333.html "XEP-0333") |
| HTTP File Upload | [XEP-0363](https://xmpp.org/extensions/xep-0363.html "XEP-0363") |
| OMEMO | [XEP-0384](https://xmpp.org/extensions/xep-0384.html "XEP-0384") |

## Installation (build it yourself):
1. Install [Visual Studio 2017](https://www.visualstudio.com/de/downloads) with:
   - the **C++ (v141) Universal Windows Platform tools**, and
   - the **10.0.15063** and **10.0.16299** Windows 10 SDKs.
2. Clone the repo and open the solution in Visual Studio 2017.
3. Set the configuration to **Release / ARM**.
4. Build. To produce a sideload package: **Project -> Store -> Create App Packages ->
   Sideloading -> ARM / Release**.
5. To build from source you need your own signing certificate — create one via
   `Package.appxmanifest -> Packaging -> Choose Certificate -> Create`.
6. Install the resulting `.appx` on a developer-unlocked Windows 10 Mobile device.
   [Here](https://docs.microsoft.com/en-us/windows/uwp/get-started/enable-your-device-for-development)
   is more on enabling developer mode and sideloading UWP apps.

## References:
This project wouldn't be possible without the great work of all those people working
on the libraries used by UWPX.
[Here](https://uwpx.org/about/) you can find a list of all libraries and other
references used for UWPX development.

## Credits
All application code is by the upstream [UWPX](https://github.com/UWPX/UWPX-Client)
authors. This repository only backports/retargets that work to Windows 10 Mobile.

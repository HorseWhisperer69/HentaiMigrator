## Introduction
A user-friendly command line tool to help you automatically migrate your Hentai favorites to/from NHentai and E-Hentai.
HentaiMigrator supports automatic export, conversion, and import of favorites -
all you need to do is connect your accounts and HentaiMigrator will do all the hard work for you.

## Get started
> [!WARNING]
> HentaiMigrator makes a lot of requests to the individual Hentai repositories.
> As such, it is recommended to connect to a VPN to prevent your home IP address from getting banned
> (bans typically last for 1h).

[Releases](https://github.com/AnteDeliria/HentaiMigrator/releases)

Head on over to the releases page and download the latest executable for your platform.
HentaiMigrator will guide you through the migration process. Progress is saved automatically
in case the program crashes mid migration.

### Retrieving your User-Agent and Cookie
In order to migrate your favorites, HentaiMigrator needs to authenticate with
your Hentai repository accounts. This is done by supplying your **User-Agent**
and **Cookie**. Please see the guide below, adapted from[^1], on how to do this:

1. **Open NHentai/E-Hentai in your browser**: Navigate to the NHentai/E-Hentai website.
2. **Login to the website**: If you are not already logged-in, do so as you would normally.
      - **For E-Hentai**: If your account can access it, switch your website to https://exhentai.org/. Some content
        is restricted on https://e-hentai.org and switching ensures that the retrieved Cookie can be used to query it.
4. **Open Developer Tools**:
      - **For Google Chrome**: Right-click on the webpage and select Inspect or simply press Ctrl + Shift + I (or Cmd + Option + I on Mac).
      - **For Firefox**: Right-click on the webpage and select Inspect Element or press Ctrl + Shift + I (or Cmd + Option + I on Mac).
5. **Navigate to the Network Tab**: In the Developer Tools panel, click on the Network tab. This tab captures all network requests made by the webpage.
6. **Reload the Page**: With the Network tab open, reload the website by pressing Ctrl + R (or Cmd + R on Mac). This ensures that all network requests are captured.
7. **Select the first nhentai.net/e-hentai.net Request**: After reloading, you'll see a list of files on the left side of the Network tab.
   Click on the first file with Status 200 and Name nhentai.net/e-hentai.org depending on which website you are on. This represents the main request to the website.
8. **Find the Request Headers**: On the right side, you'll see several tabs like Headers, Preview, Response, etc. Make sure you're on the Headers tab. Scroll down until you find a section named Request Headers.
9. **Copy either the User-Agent or Cookie and paste it into HentaiMigrator when prompted**:
      - **User-Agent**: This is a string that tells the server which web browser is being used. Look for an entry named User-Agent and copy its value.
      - **Cookie**: This is a string that stores information about your current session. Look for an entry named Cookie and copy its value.
           - **For NHentai**: Make sure the string contains `csrftoken=` and `sessionid=`.
           - **For E-Hentai**: Make sure the string contains `ipb_member_id=` and `ipb_pass_hash=`.


[^1]: [Enma - a Python library designed to fetch and download manga and doujinshi data
from many sources including Manganato and NHentai.](https://github.com/AlexandreSenpai/Enma)

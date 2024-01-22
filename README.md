# veeam-host-rename-or-move
A small C# CLI application that uses Microsoft.Isam.Esent.Interop to safely modify the values in ConfigDb\config.edb to allow Veeam Backup for 365 (VBO) configuration to be moved to a new server, or the server to be renamed.

![Screenshot](https://github.com/platima/veeam-host-rename-or-move/assets/13729856/69f4b2e3-c484-4916-9623-59b93ace895c)

I don't think it safely captures the EDB opening read-only, or checks that the service is stopped.

Currently it also does not change the FQDN field either, as that appears to update itself. You may need to manually update your XML file too.

Feel free to send any PRs through if you improve it.

CHeers

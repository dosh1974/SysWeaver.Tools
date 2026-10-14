# AcmeCertUtil

[⬆ SysWeaver.Tools overview](../README.md)

> Obtains (or renews) a Let's Encrypt certificate for a domain through ACME and saves it as a password protected `.pfx` file.

| | |
|---|---|
| **Layer** | Tool (command line) |
| **Kind** | Command line tool |

## Purpose

Gets a browser-trusted certificate for a server that doesn't run a SysWeaver application (or before it runs one), ex: for another web server or for manual installation.

## How it fits into SysWeaver

A command line front end for `AcmeCertificateProvider` in [SysWeaver.Security.Acme](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Security.Acme/README.md): the provider orders the certificate from the Let's Encrypt production directory and answers the HTTP challenge itself, with a temporary HTTP server on port 80. It isn't part of `SysWeaver/_tools`.

## Using it

```
AcmeCertUtil <email> <domainName> [password] [renew] [filename.pfx]

  email          Account e-mail address for the certificate authority
  domainName     The domain to get a certificate for
  password       The .pfx password (default: the email)
  renew          "renew" (or true, yes, 1) deletes the existing certificate file and orders a new one,
                 "norenew" (or false, no, 0, default) keeps an existing certificate while it's valid
  filename.pfx   The certificate file (default: <domainName>.pfx)
```

The full path of the certificate file is printed when done.

Exit codes: 0-2 as all tools (1 = failed to get the certificate, 2 = wrong number of arguments or an invalid renew value), see [EXIT CODES](../README.md#exit-codes).

## Limitations and considerations

- The domain must resolve to the machine running the tool and port 80 must be reachable from the internet (and free) during validation
- Let's Encrypt rate limits apply; an existing valid certificate is reused unless `renew` is given
- The account key is stored unencrypted, see the [SysWeaver.Security.Acme](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Security.Acme/README.md) limitations

## Relationships

- **Project:** [`AcmeCertUtil.csproj`](AcmeCertUtil.csproj)
- **Builds on:** [SysWeaver.CommandLine](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.CommandLine/README.md), [SysWeaver.Security.Acme](https://github.com/dosh1974/SysWeaver/blob/main/SysWeaver.Security.Acme/README.md)

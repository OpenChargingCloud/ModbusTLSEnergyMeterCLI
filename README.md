# Modbus/TLS Energy Meter

[![CI](https://github.com/OpenChargingCloud/ModbusTLSEnergyMeterCLI/actions/workflows/ci.yml/badge.svg)](https://github.com/OpenChargingCloud/ModbusTLSEnergyMeterCLI/actions/workflows/ci.yml)
[![Nightly](https://github.com/OpenChargingCloud/ModbusTLSEnergyMeterCLI/actions/workflows/nightly.yml/badge.svg)](https://github.com/OpenChargingCloud/ModbusTLSEnergyMeterCLI/actions/workflows/nightly.yml)

A simulated three-phase energy meter that speaks **Modbus/TLS** (*mbaps*, the
registered port 802) and nothing else: there is no plaintext listener, every
peer must present a client certificate, and what that peer may do is decided by
the **SunSpec role** carried in an X.509v3 extension of its certificate.

Start it on a Linux box, point a Modbus/TLS client at it, and you have a meter
to talk to - one that behaves like the real thing all the way down to refusing
the requests it ought to refuse.

It is meant to stand in for the meters that
[ChargingStationCLI](https://github.com/OpenChargingCloud/ChargingStationCLI) and
[LocalControllerCLI](https://github.com/OpenChargingCloud/LocalControllerCLI)
need for their own use cases.

This repository is the **command line**: reading arguments, building a PKI when
there is none, printing what somebody who just started it needs to know, a
prompt for whoever is at its console, and stopping on `quit`, Ctrl+C or SIGTERM.
The meter itself - the registers, the simulation, the web interface, the log and
the two APIs - is a library of its own in
[`libs/ModbusTLSEnergyMeter`](libs/ModbusTLSEnergyMeter/README.md), so that a
test, a service or another program can host one without starting a process.
**What the meter is and does is described there.**


## Getting it running

```bash
git clone --recurse-submodules https://github.com/OpenChargingCloud/ModbusTLSEnergyMeter.git
```

```bash
dotnet build ModbusTLSEnergyMeter/ModbusTLSEnergyMeter.slnx
```

```bash
./run.sh --any --port 5802
```

Building needs the .NET 10 SDK and **Node 22 or newer**: the web interface is
bundled by webpack as part of the build. `-p:SkipFrontendBuild=true` leaves it
out and keeps the rest.

At the first start there are no certificates yet, so it builds a whole PKI for
itself under `pki/` and then listens. Everything it prints afterwards - where it
listens, which certificate it shows, which client certificates exist and what
each of them is allowed to do - is what you need to connect to it.

To check from a second terminal that it really works:

```bash
./run.sh --selftest --port 5802
```

which connects as a client, reads the whole register block and prints it:

```
  reading        127.0.0.1:5802
  as             ReadOnlySunSpec
  client cert    CN=EnergyMeter01-Client-ReadOnlySunSpec, O=OCC Energy Meters, C=DE

  manufacturer   Vanaheimr
  model          DemoMeter-3P
  options        tls-only
  version        1.0.0
  serial         EnergyMeter01
  unit address   1
  model          213 (length 24)

                 voltage      current         power
  L1               229.6 V       4.10 A        -943 W
  L2               229.2 V       4.12 A        -946 W
  L3               230.4 V       3.87 A        -892 W
  total            229.7 V      12.09 A       -2781 W

  frequency      50.00 Hz
  exported       10 Wh
  imported       0 Wh

  meter mode     2 - in front of a generator: exporting only
                 (register 40094, writable from GridServiceSunSpec up)
```

That one is a meter in front of a generator, so the power is negative and only
the exported counter moves. A meter in front of a load reads the other way
round, and one at the grid connection point reads whichever way the site is
going at that moment.

`--help` lists every option.


## Typing at it

Once it is up, the console is a prompt rather than a place that only scrolls:

```
EnergyMeter01> syncNTS
succeeded after 601 ms: 4 of 4 server(s) answered (2 required), offset +1008.6 ms, spread 4.1 ms
  ptbtime1.ptb.de  +1009.2 ms, round trip 37.0 ms, key exchange new
  ptbtime2.ptb.de  +1010.2 ms, round trip 37.0 ms, key exchange new
  ptbtime3.ptb.de  +1007.9 ms, round trip 37.0 ms, key exchange new
  ptbtime4.ptb.de  +1006.1 ms, round trip 44.4 ms, key exchange new
```

The prompt is the meter's serial number, because a meter usually runs on a
bench beside a charging station, a vehicle and often a second meter, and each
console should say which of them it is. `help` lists what can be typed, `quit`
leaves, **Tab** completes and the **up arrow** walks back through what was typed
before.

`syncNTS` is the first command, and it is **Check the clock now** on the **NTS**
page, typed: the same group of time servers is asked, the same entries go into
the log, and the same result is left behind for the page to show as the last
synchronisation. The one entry that differs says who asked - the page names the
account that pressed the button, the prompt says it was somebody at the command
line. Neither of them steps the clock.

With one of the meter's time servers after it, it is that server's **Test**
button instead: one server, on the ports it is configured with, and every step
of the key exchange and the time request with when it happened. Only a server of
this meter is tested; anything else is answered with the ones there are, and
nothing is asked. **Tab** offers the meter's own as soon as the command is
typed, completing as far as their names agree.

The key exchange is TLS, and the test says what its certificate claims and
whether that held up: the session, then every certificate of the chain as this
machine built it - the server's, the intermediates', the root's - each with both
ends of its validity and the days it has left, the root's SHA-256 fingerprint,
and the verdict with its reasons. The root is there as much as the server's
certificate because a root can be pinned, and a pinned root that runs out stops
everything relying on it; which root the chain ends at depends on the machine's
trust store. A certificate that is refused is described just the same, before
the exchange is said to have failed.

```
EnergyMeter01> syncNTS ptbtime2.ptb.de
ptbtime2.ptb.de answered, 1461 ms altogether:
     +1 ms  Asking ptbtime2.ptb.de: key exchange on port 4460, time on port 123, 10 second(s) allowed.
   +597 ms  'ptbtime2.ptb.de' resolves to 192.53.103.104, 2001:0638:0610:be01:0000:0000:0000:0104.
   +597 ms  Key exchange over TLS ...
  +1339 ms  Connected to 192.53.103.104, of 2 address(es) that were offered.
  +1339 ms  Where the time went: name 78 ms, TCP 62 ms, TLS 527 ms, key exchange 36 ms.
  +1352 ms  TLS 1.3, TLS_AES_128_GCM_SHA256, ALPN ntske/1.
  +1354 ms  Server certificate: CN=ptbtime2.ptb.de, for ptbtime2.ptb.de; RSA 3072-bit, sha256RSA; valid 2026-08-09 03:05:52 to 2026-11-07 03:05:51 UTC, 43 day(s) left.
  +1355 ms  Intermediate CA: CN=YR1, O=Let's Encrypt, C=US; RSA 2048-bit, sha256RSA; valid 2025-09-03 00:00:00 to 2028-09-02 23:59:59 UTC, 709 day(s) left.
  +1355 ms  Intermediate CA: CN=Root YR, O=ISRG, C=US; RSA 4096-bit, sha256RSA; valid 2026-05-13 00:00:00 to 2032-09-02 23:59:59 UTC, 2170 day(s) left.
  +1355 ms  Root CA: CN=ISRG Root X1, O=Internet Security Research Group, C=US; RSA 4096-bit, sha256RSA; valid 2015-06-04 11:04:38 to 2035-06-04 11:04:38 UTC, 3174 day(s) left.
  +1355 ms  The root's SHA-256 fingerprint: 96bcec06264976f37460779acf28c5a7cfe8a3c0aae11a8ffcee05c0bddf08c6.
  +1355 ms  Validated: the chain ends at a root this machine trusts, nothing in it is revoked (asked online), and 'ptbtime2.ptb.de' is one of the server certificate's names.
  +1367 ms  The key exchange succeeded: AES_SIV_CMAC_256, 8 cookie(s).
  +1367 ms  It named no NTP server of its own, so the time is asked of this host.
  +1367 ms  Authenticated NTP request ...
  +1459 ms  Answered by 192.53.103.104:123; 8 cookie(s) left, and a fresh one came back.
  +1459 ms  Round trip 26.9 ms.
  +1460 ms  This meter's clock is +1050.0 ms off what ptbtime2.ptb.de says.
  +1460 ms  The clock was not stepped: that is a different thing, with every signed reading hanging off it, and not something a test does by surprise.
```

The log keeps writing while you type, from whichever thread did the thing it is
reporting, and your half-typed line survives it: the line is taken off the
screen, the entry is written whole, and the line comes back with the cursor
where it was.

Where there is no terminal - from a script, under a service manager, in CI, or
with the output going into a file or through `| tee` - there is no prompt, and
the meter runs until it is stopped, exactly as it did before.


## What the command line decides

### Which meter this is

```bash
./run.sh --meter-mode import --serial GridMeter7
```

`--meter-mode` says where this meter sits, and therefore which way energy flows
through it: `net` at the grid connection point (it draws at night and feeds back
around noon, so the power is signed and both counters move), `import` in front
of a load - which is what a charging station's meter is - or `export` in front
of a generator, which reads zero at night because the sun is down. It can be
changed afterwards through register 40094 or on the web page.

`--sim-day <minutes>` compresses the simulated day, so that a whole one can be
watched over a coffee instead of over a day. It speeds up the load and the sun
only; the energy counters still count real seconds.

### Where it listens

`--port` for Modbus/TLS, `--http-port` for the web interface, `--any` to listen
on every address rather than on the loopback - which applies to both.

`--https` serves the web interface over TLS. Its certificate is **its own**, in
a store of its own: what a charging station checks and what a browser checks
come from different places and are never the same file. At the first start the
meter signs one for itself so that the page can be reached at all - a browser
will say it does not know who signed it, and it is right. Ask for a proper one
on the Web certificate page, and it takes over the moment it is valid.

### Where it keeps things

`--data` for the accounts, the log and the certificate stores, `--config` for
the name servers and the time server, `--pki` for the certificates this meter is
bootstrapped with. All three default to a directory beside the repository root,
and all three are ignored by git: the generated PKI holds private keys for a
simulator, but private keys all the same.

The certificate this meter was started with is taken into the store at the first
start, and the CA it was started with into the list of accepted client CAs - so
that from then on there is one place that decides what is shown and who is let
in, and a meter started the old way needs nothing done to it.

`--log-days` is how many days of the log stay on disk; `0` keeps it in memory
only.


## Checking it

```bash
./run.sh --selftest [<role>] --port 5802
```

connects as a client with one of the generated certificates, walks the whole
path a real client walks - TLS, the client certificate, the role in it, the
authorization policy and the register map - and prints what the meter said. The
shortest answer to "is the thing I just started on that Linux box actually
working".

Running it with the certificate that carries no role is how you check the policy
still holds:

```bash
./run.sh --selftest NO-ROLE --port 5802
```

That one passes when the meter **refuses**.

[CI](.github/workflows/ci.yml) asks both on every push, of a meter it has just
started on Debian with the PKI that first start built: the first has to read the
registers, the second has to be refused.

```bash
./run.sh --verify-log
```

does not serve anything: it walks the log on disk and checks every line against
its own hash, the line before it and the signature, then says what it found and
prints the head of the chain. What that is worth, and what it is not, is
[in the library's README](libs/ModbusTLSEnergyMeter/README.md#and-it-is-signed).


## The port

802 is the port IANA registered for Modbus/TLS, and it is also below 1024, so on
Linux binding it takes either root or a capability granted once:

```bash
sudo setcap cap_net_bind_service=+ep ModbusTLSEnergyMeterCLI/bin/Release/net10.0/ModbusTLSEnergyMeterCLI
```

`--port` picks another one when neither is wanted.


## Reaching it from another machine

The generated meter certificate covers `localhost`, `<name>.local` and both
loopback addresses. A client on a different machine checks the name it dialled
against that certificate, so tell the PKI which names and addresses those are:

```bash
./run.sh --any --regenerate-pki --name GridMeter7 --hostname meter7.lan --ip 192.168.7.20
```

`--hostname` and `--ip` are repeatable, and only reach a certificate while it is
being built - so an existing PKI needs `--regenerate-pki`, which invalidates
every client certificate handed out so far.

`--server-pfx`, `--pfx-password` and `--client-ca` use certificates from
somewhere else instead, and then nothing is generated at all.


## The first administrator

At the first start there are no accounts, so one is made and its password
printed once:

```
  +- First start: there were no accounts, so an administrator was made -------
  |  user      admin
  |  password  FNCiigxUCU5wLo_3Q1Ma3dSqz9ooO1fz
  |  It is shown here once and kept only as a hash. Write it down.
  +---------------------------------------------------------------------------
```

That account is an `IsAdmin` of the organization `EnergyMeter`, and the web
interface is at `http://127.0.0.1:2351/` unless `--http-port` or `--any` said
otherwise.

It is not meant to stay the only one. Under Configuration -> Accounts it makes
more and gives each a role, three of which change nothing at all - so that
somebody who has to watch this meter does not have to be handed the account that
can also clear its energy counters. The last administrator cannot be demoted or
removed: a meter with none left is one nothing can be done to from a browser
again.


## Repository

| | |
|---|---|
| `ModbusTLSEnergyMeterCLI/` | the arguments, the PKI bootstrap, the banner, `--selftest`, `--verify-log`, and the prompt with its commands in `CLI/` |
| `libs/ModbusTLSEnergyMeter/` | [the meter itself](libs/ModbusTLSEnergyMeter/README.md) |
| `libs/Hermod/` | HTTP, DNS, and the SunSpec Modbus/TLS frontend and device |
| `libs/Norn/` | the NTS client |
| `libs/Styx/` | collections, pipes, and the command line the prompt is built on |


### Acknowledgements

This software is partly sponsored by the [NLnet Zero Commons Fund](https://nlnet.nl/commonsfund/) as part of the [EVQI](https://nlnet.nl/project/EVQI/) project and within this tested against the [Apache PLC4x](https://plc4x.apache.org) project.    
It is also part of the security extensions for the **Open Charge Point Protocol**.

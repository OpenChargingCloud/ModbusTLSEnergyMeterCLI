# Modbus/TLS Energy Meter

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


## Getting it running

```bash
git clone --recurse-submodules https://github.com/OpenChargingCloud/ModbusTLSEnergyMeter.git
cd ModbusTLSEnergyMeter
dotnet build ModbusTLSEnergyMeter.slnx
./run.sh --any --port 5802
```

The meter itself lives in `libs/ModbusTLSEnergyMeter`, so that a test, a service
or another program can host one; `ModbusTLSEnergyMeterCLI` is the command line
around it.

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
  manufacturer   Vanaheimr
  model          DemoMeter-3P
  serial         EnergyMeter01

                 voltage      current         power
  L1               230.9 V      10.12 A        2336 W
  L2               229.2 V      10.48 A        2402 W
  L3               229.3 V       9.89 A        2267 W
  total            229.8 V      10.16 A        7005 W

  frequency      49.99 Hz
```

`--help` lists every option.

## Signing in

Beside the Modbus/TLS listener there is an HTTP server, on port 2351 unless
`--http-port` says otherwise, where a person signs in to administer the meter.
Accounts, organizations and sessions are Hermod's `HTTPExtAPI`, and the role
somebody holds in the meter's organization - `IsAdmin`, `IsAdminReadOnly`,
`IsMember`, `IsGuest` - is what separates who may change this meter from who
may only watch it.

At the first start there are no accounts, so one administrator is made and its
password printed once:

```
  +- First start: there were no accounts, so an administrator was made -------
  |  user      admin
  |  password  FNCiigxUCU5wLo_3Q1Ma3dSqz9ooO1fz
  |  It is shown here once and kept only as a hash. Write it down.
  +---------------------------------------------------------------------------
```

That account is an `IsAdmin` of the organization `EnergyMeter`. Accounts live
under `--data`, which defaults to `data/` below the repository root.

Three things are served, and each answers for itself because Hermod dispatches
to the most specific of them first:

| | |
|---|---|
| `/` | the web interface |
| `/accounts` | signing in, users, organizations - Hermod's `HTTPExtAPI` |
| `/api/v1` | this meter's own JSON API |

Signing in is a `POST` to `/accounts/auth/login` with
`{"login": ..., "password": ...}`, which answers with the session cookies that
every resource below is read with.

## The web interface

The same frame a charging station wears - a menu on the left, a page on the
right - so that somebody looking after both does not have to learn two
interfaces. It is one HTML file with its stylesheet and its script inside it,
embedded in the assembly: a meter is one binary to deploy and needs nothing
installed beside it, and no build step of its own has to be kept working.

Pages: the meter and what it is measuring, the DNS client, the NTS client with
the state of the clock, the certificates, and the log. The DNS and NTS pages are
forms - name servers can be added and removed, timeouts and ports changed, and
the time authority named - and each save writes the configuration file before
the change takes effect. What somebody may not do is not offered: the buttons
are absent rather than disabled-and-refused, though every request is checked
again on arrival, so a browser that puts them back gains nothing but a 403.

## The JSON API

Everything below `/api/v1` needs the session cookie, and each resource names the
permission it wants. What a person may do follows from their role in the
organization `EnergyMeter`:

| Role | read meter | read config | change DNS/NTS | diagnostics | write registers |
|------|:----------:|:-----------:|:--------------:|:-----------:|:---------------:|
| `IsAdmin`          | yes | yes | yes | yes | yes |
| `IsAdminReadOnly`  | yes | yes | no  | yes | no  |
| `IsMember`         | yes | yes | no  | no  | no  |
| `IsGuest`          | yes | no  | no  | no  | no  |

A role this meter does not know, and an account belonging to no organization of
it, grant nothing at all.

| Resource | |
|----------|---|
| `GET  /api/v1/me` | who is signed in, their role and their permissions |
| `GET  /api/v1/status` | serial, uptime, both listeners |
| `GET  /api/v1/meter` | the readings, scale factors applied |
| `GET  /api/v1/meter/registers?start=&count=` | the raw register block |
| `PUT  /api/v1/meter/mode` | `{"mode": 0\|1\|2}` |
| `POST /api/v1/meter/energy/reset` | clear the energy counters |
| `GET  /api/v1/configuration` | every section at once |
| `GET/PUT /api/v1/configuration/dns` | how it resolves names |
| `GET/PUT /api/v1/configuration/nts` | where it reads the time |
| `POST /api/v1/configuration/nts/sync` | check the clock now |
| `GET  /api/v1/configuration/time` | what time it is, and what that is worth |
| `GET  /api/v1/configuration/certificates` | which certificates it was started with |
| `GET  /api/v1/logs?limit=&after=&tag=` | what happened, newest last |
| `GET  /api/v1/events` | the same as a Server-Sent Events stream |

A `PUT` writes the configuration file before the change takes effect, and
answers with the section as it now stands. Unknown paths below `/api` answer
with a JSON 404 rather than falling through to the accounts.

```bash
curl -c jar -X POST http://127.0.0.1:2351/auth/login -H 'Content-Type: application/json' -d '{"login":"admin","password":"..."}'
```

```bash
curl -b jar http://127.0.0.1:2351/api/v1/meter
```

State-changing requests are refused when a browser says they came from another
site, and 403 is used rather than 401 where signing in again would not help.

## The log

Everything that happens inside this meter goes into one log: **every Modbus
request, allowed or refused**, every clock check, every change somebody made
and who made it, and whatever Hermod says while doing its part. The last 2000
entries are kept in memory, the console shows them as they happen, and a browser
follows the same log over `/api/v1/events`.

It survives a restart. Every entry is also written as one line of JSON to
`<--data>/logs/meter-YYYY-MM-DD.jsonl`, and the newest of them are read back at
the next start - numbering included, so that a browser following the log is not
handed entries it has already seen. One file per day, thirty days kept
(`--log-days`, `0` keeps the log in memory only). One line per entry because
that is the format that survives being read by something other than this
program: grep finds a line, jq takes it apart, and a file truncated by a power
cut loses its last line and nothing else.

### And it is signed

Every line carries the hash of the line before it and a signature of its own:

```json
{ "id": 16, "timestamp": "...", "level": "warning", "tags": ["modbus","request","denied"],
  "message": "(none) 0x03 @40000+98 -> refused: no role extension in client cert",
  "data": { ... },
  "prev": "wJ8...=", "key": "d13924a2fd92f261",
  "hash": "1xx...=", "sig": "MEUCIQ...==" }
```

The key is ECDSA over P-256, made at the first start and kept in
`logs/signing-key.pem` (mode 600); the public half sits beside it as
`signing-key.pub.pem`. Its own key rather than the meter certificate: those
answer different questions, and a certificate that is reissued would leave the
old log needing the old certificate for ever.

Checking it is one question, asked deliberately:

```bash
./run.sh --verify-log
```

or `GET /api/v1/logs/verify`, or the **Check the log** button on the Logs page.
Three things are checked per line, and they catch different things - the hash
catches a line that was edited, the chain catches a line that was removed,
moved or inserted, and the signature catches a line written by something that
did not have this meter's key.

**What this is worth, and what it is not.** The key sits next to the log it
signs, so somebody who can write that directory can also read the key and sign
a log of their own invention. Signing catches a file that was edited, truncated
in the middle, reordered, or copied from another meter; it does not catch an
attacker who took the key. Cutting the *end* off a log is not caught either -
every remaining line is genuine and the file cannot know how long it was meant
to be. What catches both is the `head`, the hash of the newest line, reported by
all three of the routes above: write it down somewhere this meter cannot reach,
and a log that no longer leads to it has been rewritten no matter how well it
signs itself.

Pruning breaks the chain on purpose: the oldest file left begins with a line
pointing at a day that was thrown away, and a check of the whole log says so.

```bash
jq -r 'select(.tags | index("denied")) | "\(.timestamp) \(.data.peer) \(.data.denyReason)"' data/logs/meter-*.jsonl
```

A request carries the whole of what was decided about it:

```json
{ "connectionId": 3, "peer": "127.0.0.1:50665", "role": null,
  "unitId": 1, "transactionId": 1, "functionCode": "0x03", "function": "ReadHoldingRegisters",
  "address": 40000, "quantity": 98,
  "allowed": false, "denyReason": "no role extension in client cert",
  "exceptionCode": "IllegalFunction", "responseBytes": 2, "duration_ms": 0.4 }
```

Refusals are warnings and carry the tag `denied`, so "show me everything that
was turned away" is one filter rather than a search through everything that was
not. A meter that recorded only what it permitted could not answer that
question afterwards at all.

Reading the log needs `ReadConfiguration` and not merely `ReadMeter`: it holds
the addresses peers connect from and every certificate that was turned away,
which is more than somebody allowed to watch the readings was given.

## Name resolution and the time

Both are read from `--config` (`configuration.json` below the repository root
by default), in the same two sections that a LocalController uses, so one file
can be written once and copied:

```json
{
  "dns": { "enabled": true, "servers": [ "udp://192.168.1.1:53" ] },
  "nts": { "enabled": true, "hostname": "ptbtime1.ptb.de", "checkEvery": "00:15:00",
           "legalTimeAuthority": "PTB" }
}
```

A section that is absent is not a section set to nothing: it means the file has
no opinion, and the system default stands. The clock is checked against the NTS
server on that interval - authenticated, and without stepping the meter's own
clock - because a reading is only worth what the timestamp on it is worth.

### The port

802 is the port IANA registered for Modbus/TLS, and it is also below 1024, so on
Linux binding it takes either root or a capability granted once:

```bash
sudo setcap cap_net_bind_service=+ep ModbusTLSEnergyMeterCLI/bin/Release/net10.0/ModbusTLSEnergyMeterCLI
```

`--port` picks another one when neither is wanted.

### Reaching it from another machine

The generated meter certificate covers `localhost`, `<name>.local` and both
loopback addresses. A client on a different machine checks the name it dialled
against that certificate, so tell the PKI which names and addresses those are:

```bash
./run.sh --any --regenerate-pki --name GridMeter7 --hostname meter7.lan --ip 192.168.7.20
```

`--server-pfx`, `--pfx-password` and `--client-ca` use certificates from
somewhere else instead, and then nothing is generated at all.


## Roles

Every client certificate carries exactly one role in the extension
`1.3.6.1.4.1.50316.802.1`, as an ASN.1 `UTF8String` - the Modbus.org PEN, per
[MBTLS] §8.4 and SunSpecTCP-29..31. The meter reads it during the handshake and
decides each request against it:

| Role | read | write commanded | write protected |
|------|:----:|:---------------:|:---------------:|
| `ReadOnlySunSpec`             | yes | no  | no  |
| `GridServiceSunSpec`          | yes | yes | no  |
| `NetworkAdministratorSunSpec` | yes | yes | yes |
| `SuperAdministratorSunSpec`   | yes | yes | yes |

A certificate without a role - `client-NO-ROLE.pfx` in the generated PKI - gets
Modbus exception 01 for everything, as SunSpecTCP-32 requires. Running the
self-test with it is how you check the policy still holds:

```bash
./run.sh --selftest NO-ROLE --port 5802
```

That one passes when the meter refuses.

The decision is taken in the TLS frontend, once per request, before a single
Modbus byte reaches the device behind it.


## The registers

SunSpec Common Model 1 followed by a subset of Meter Model 213, based at 40000:

| Address | Contents |
|---------|----------|
| 40000 - 40001 | `SunS` marker |
| 40002 - 40069 | Common Model 1: manufacturer, model, options, version, serial |
| 40068 | unit address - **protected** |
| 40070 - 40071 | Meter Model 213 header, id and length |
| 40072 - 40076 | current: total, L1, L2, L3, scale factor (-2) |
| 40077 - 40081 | voltage: average, L1, L2, L3, scale factor (-1) |
| 40082 - 40083 | frequency, scale factor (-2) |
| 40084 - 40088 | power: total, L1, L2, L3, scale factor (0) |
| 40089 - 40093 | energy exported, energy imported, scale factor (0) |
| 40094 | meter mode - **commanded** |
| 40095 | reset energy - **commanded**, write `0xCAFE` to clear the counters |
| 40096 - 40097 | end-of-models marker |

Everything not marked is read-only; a write to it is refused whatever the role.
Measurements move once a second.

Addresses here are absolute, as SunSpec writes them. Clients that count
registers from one - Hermod's own Modbus client among them - need the usual
offset of 1.


## What it does not do yet

* The simulation only ever **exports**: register 40089 climbs, and imported
  energy at 40091 stays at zero. The meter-mode register at 40094 is accepted
  and stored, but does not yet change what the simulation does. A charging
  station's consumption meter therefore has nothing to read yet.
* One meter per process. The unit identifier in the frame is not used to route,
  so several meters means several instances on several ports.
* New accounts have to be made in code. Hermod's user-creation routes are
  commented out in this version, so only the first administrator appears by
  itself; the tests in `ModbusTLSEnergyMeterTests` show how to add more. There
  is no page for accounts either.
* A refused request is written down twice: once by the Modbus/TLS frontend in
  Hermod, which says `RBAC DENY ...` as it always did, and once by this meter
  as the audit record above. The second is the one with the data attached; the
  first is not ours to remove.
* Nothing carries the head of the log anywhere else by itself. Until something
  does - a syslog sink, a witness, a line in somebody's notes - the signing
  proves the files were not edited, and not that they are all the files there
  were. See the caveat above.


### Acknowledgements

This software is partly sponsored by the [NLnet Zero Commons Fund](https://nlnet.nl/commonsfund/) as part of the [EVQI](https://nlnet.nl/project/EVQI/) project and within this tested against the [Apache PLC4x](https://plc4x.apache.org) project.    
It is also part of the security extensions for the **Open Charge Point Protocol**.

# Skull Wins 0.20

Gemini, identities, and the profile travels with the executable.


## Gemini

Capsules load. TLS 1.2 or 1.3 on port 1965, gemtext rendered, redirects
followed and stopped at five as the specification requires, input prompts shown
as a page, failures named rather than numbered.

Certificates use trust on first use, because gemini servers are self-signed as
a matter of course and ordinary validation would refuse nearly every capsule
that exists. A host is pinned the first time you see it. The same certificate
passes. A different one while the pinned certificate is still valid is refused,
because that is what an interception looks like.

That last part was verified by planting a false pin and confirming the
handshake was refused with an empty body. It is the only way to know pinning is
real rather than decorative: a validation callback that simply returns true
gives TLS that protects nothing, and every page still loads.

    :cert             what is pinned for this host
    :cert accept      trust a changed certificate, deliberately
    :cert forget      unpin, so the next visit pins afresh

Those three close a hole in 0.12's design. Pinning had no escape hatch, so a
capsule that rotated its certificate early was refused and stayed refused
forever. A security measure with no way out is a way to lose a site.


## Identities

An identity is a client certificate. It is how a capsule knows you between
visits, and gemini has nothing else resembling a login.

    :identity             list them
    :identity new NAME    make one
    :identity use NAME    attach it to this capsule and directory
    :identity drop        stop using it here
    :identity forget NAME delete it and its key

Nothing is generated for you. The specification says a client must not make a
certificate and repeat a request without the user being involved, so a capsule
asking for one gets you a page explaining what it wants and naming the command.

An identity covers the directory it was attached in and everything below it, so
attaching while reading one post covers its siblings and nothing above them.

The key file has no passphrase, like an unencrypted SSH key: whoever holds the
file is you. Under a portable profile that file sits beside the executable.
Loading one also imports its key into a container on the machine you are on,
which is what Windows requires before TLS will use it, so a portable copy is
not quite traceless.

Gopher has no identities. The protocol has no authentication to attach one to,
and the identities page says so rather than pretending otherwise.


## The profile travels

Data lives in `skull-data` beside the binary when that folder can be written
to, so a copy on a stick carries its history, its pinned certificates and its
identities with it. When it cannot, because the program sits under Program
Files or on a locked stick, it falls back to `%APPDATA%\skull` and says which
one it chose.

    --portable        insist on beside the executable
    --no-portable     insist on %APPDATA%
    --profile=PATH    somewhere else

An existing `%APPDATA%` profile is copied across once, the first time a
portable copy runs. Without that, upgrading looks exactly like losing your
history.


## It will not become your default browser

No protocol association, no registry writes, nothing outside its own profile.
Links clicked in other programs will not open here, and that is deliberate
rather than missing.

That was the only real reason an installer existed, so the portable single file
is now the only format.


## What is still not here

Ad blocking, form filling, user stylesheets, proxies, tab groups, private mode,
session restore. Close the browser with ten tabs open and they are gone.


## Numbers

285 tests. One executable, about 60 MB, carrying its own runtime.


## Licence

GNU GPLv3.

Pablo Murad, https://pablomurad.com
https://github.com/runawaydevil/skull_browser_win


## Checksum

    sha256  57a69391888a27e1d6ee021619f29ef04394e619016e677e2747d74a26e4722b
            skull-0.20-win-x64.exe

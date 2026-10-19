# Third-party components

## PDFium

- Upstream project: <https://pdfium.googlesource.com/pdfium/>
- Binary distributor: <https://github.com/bblanchon/pdfium-binaries>
- Pinned release: `chromium/8100`, PDFium `157.0.8100.0`
- Platform: Windows x64, without V8 or XFA
- Archive: <https://github.com/bblanchon/pdfium-binaries/releases/download/chromium/8100/pdfium-win-x64.tgz>
- Archive SHA-256: `51621bcc1d9be9ea448f4b84b0516862b928e3968d3fca899af1a6d6e2848d6d`

The dependency script verifies this checksum before extraction. Application builds use the unmodified `bin/pdfium.dll` and copy the archive's `LICENSE` and complete `licenses/` directory into the distributable. The source repository does not vendor the DLL; it is included in release assets.

PDFium and bundled dependencies retain their respective license terms. See the release's `licenses/` directory for the full authoritative texts, including PDFium, Abseil, AGG, Dragonbox, fast_float, FreeType, HarfBuzz, ICU, Little CMS, libjpeg-turbo, OpenJPEG, libpng, LLVM libc, simdutf and zlib notices.

Original application code currently has no specified open-source license. The third-party notices do not license the original application code.

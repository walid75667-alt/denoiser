# Third-party components

- DeepFilterNet code and the bundled DeepFilterNet3 model are from
  https://github.com/Rikorose/DeepFilterNet at commit
  `d375b2d8309e0935d165700c91da9de862a99c31`.
  The repository offers MIT or Apache-2.0 licensing; both texts are included.
  Model SHA-256: `c94d91f70911001c946e0fabb4aa9adc37045f45a03b56008cb0c8244cb63616`.
- `native/rnnoise.dll` is the unchanged binary from the uploaded MicDenoiser archive.
  Its original README identifies RNNoise (BSD-3-Clause) and pyrnnoise
  (Apache-2.0) as its sources. That archive did not include a source revision
  or a binary build recipe. The applicable license texts are included here;
  this statement does not independently establish its build provenance.
- NAudio 2.2.1 is distributed under the MIT license. See NAudio-MIT.txt.
- The self-contained distribution includes .NET 8.0.31. The app-local
  `native/vcruntime140.dll` is a byte-for-byte copy of `vcruntime140_cor3.dll`
  from Microsoft's `Microsoft.WindowsDesktop.App.Runtime.win-x64` 8.0.31
  NuGet package, provided under the filename required by the uploaded RNNoise
  DLL. Its five imported symbols were checked against this runtime's exports.
  This compatibility copy still needs the Windows RNNoise execution check;
  it does not install or replace a system C++ runtime. See
  DotNET-WindowsDesktop-MIT.txt and DotNET-THIRD-PARTY-NOTICES.txt.
- The DeepFilterNet native dependency graph is locked in native-src/Cargo.lock.
  See the corresponding crates for their individual copyright and license notices.

The new native wrapper code is in native-src/src/lib.rs. The Windows GNU build
included with this project links its runtime statically and does not require
separate MinGW runtime DLLs.

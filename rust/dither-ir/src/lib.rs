//! # dither-ir
//!
//! The shared middle layer of the native dither-kit ports.
//!
//! Components are *encoded* on the CPU into a small display-list
//! [`Program`] (a flat `f32` array), and every per-pixel decision is made by
//! a shader that interprets that program: GLSL for GTK, Metal for SwiftUI,
//! HLSL (Direct2D) for WinUI. [`vm`] is the reference interpreter those
//! shaders mirror; it is tested pixel-for-pixel against `dither-core`.

pub mod color;
pub mod encode;
pub mod program;
pub mod vm;

pub use color::{Rgba, Tone, palette, ramp_color};
pub use encode::*;
pub use program::Program;

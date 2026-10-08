//! Small, fallible C ABI around the upstream streaming runtime.
//! The caller owns the handle and provides separate normalized float buffers.
use std::{cell::RefCell, ffi::{c_char, CStr}, panic::{catch_unwind, AssertUnwindSafe}, path::PathBuf};
use df::tract::{DfParams, DfTract, RuntimeParams};
use ndarray::{ArrayView2, ArrayViewMut2};

thread_local! { static LAST_ERROR: RefCell<String> = const { RefCell::new(String::new()) }; }

#[no_mangle]
pub extern "C" fn md_df_abi_version() -> u32 { 2 }

fn guarded<T>(fallback: T, f: impl FnOnce() -> Result<T, String>) -> T {
    match catch_unwind(AssertUnwindSafe(f)) {
        Ok(Ok(value)) => value,
        result => {
            let error = match result {
                Ok(Err(message)) => message,
                _ => "DeepFilterNet runtime panicked; stop and recreate the engine.".to_owned(),
            };
            LAST_ERROR.with(|e| *e.borrow_mut() = error);
            fallback
        }
    }
}

#[no_mangle]
pub unsafe extern "C" fn md_df_create(path: *const c_char, attenuation: f32) -> *mut DfTract {
    guarded(std::ptr::null_mut(), || {
        if path.is_null() { return Err("Missing model path".into()); }
        let path = CStr::from_ptr(path).to_str().map_err(|e| e.to_string())?;
        let params = DfParams::new(PathBuf::from(path)).map_err(|e| format!("{e:#}"))?;
        let runtime = RuntimeParams::default_with_ch(1)
            .with_thresholds(-15.0, 35.0, 35.0)
            .with_atten_lim(attenuation);
        let state = DfTract::new(params, &runtime).map_err(|e| format!("{e:#}"))?;
        Ok(Box::into_raw(Box::new(state)))
    })
}

#[no_mangle]
pub unsafe extern "C" fn md_df_set_attenuation(state: *mut DfTract, limit: f32) -> i32 {
    guarded(-1, || {
        if state.is_null() || !limit.is_finite() || !(0.0..=60.0).contains(&limit) {
            return Err("Invalid attenuation limit (expected 0..60 dB)".into());
        }
        (*state).set_atten_lim(limit);
        Ok(0)
    })
}

#[no_mangle]
pub unsafe extern "C" fn md_df_destroy(state: *mut DfTract) {
    if !state.is_null() { drop(Box::from_raw(state)); }
}

#[no_mangle]
pub unsafe extern "C" fn md_df_frame_length(state: *const DfTract) -> usize { (*state).hop_size }
#[no_mangle]
pub unsafe extern "C" fn md_df_sample_rate(state: *const DfTract) -> usize { (*state).sr }
#[no_mangle]
pub unsafe extern "C" fn md_df_delay_samples(state: *const DfTract) -> usize {
    let s = &*state;
    s.fft_size - s.hop_size + s.lookahead * s.hop_size
}

#[no_mangle]
pub unsafe extern "C" fn md_df_process(state: *mut DfTract, input: *const f32, output: *mut f32) -> i32 {
    guarded(-1, || {
        if state.is_null() || input.is_null() || output.is_null() || input == output {
            return Err("Invalid handle or overlapping audio buffers".into());
        }
        let s = &mut *state;
        let x = ArrayView2::from_shape_ptr((1, s.hop_size), input);
        let y = ArrayViewMut2::from_shape_ptr((1, s.hop_size), output);
        // The upstream result is local SNR, NOT a speech probability.
        s.process(x, y).map_err(|e| format!("{e:#}"))?;
        Ok(0)
    })
}

#[no_mangle]
pub unsafe extern "C" fn md_df_last_error(output: *mut u8, capacity: usize) {
    if output.is_null() || capacity == 0 { return; }
    LAST_ERROR.with(|e| {
        let e = e.borrow();
        let count = e.len().min(capacity - 1);
        std::ptr::copy_nonoverlapping(e.as_ptr(), output, count);
        *output.add(count) = 0;
    });
}

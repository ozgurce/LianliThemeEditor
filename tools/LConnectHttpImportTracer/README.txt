L-Connect HTTP Import Tracer

Purpose
This tool records what the local L-Connect HTTP service returns while a theme is imported or applied in L-Connect.
It does not restart L-Connect, does not edit profiles, and does not apply a theme by itself.

How to use
1. Open L-Connect and keep it running.
2. Run LConnectHttpImportTracer.exe.
3. Press ENTER in the tracer window to start tracing.
4. In L-Connect, import/download/apply the theme exactly as usual.
5. Return to the tracer window and press ENTER again.
6. Send the generated LConnectHttpImportTrace-*.zip file from the Desktop.

What it collects
- HTTP probe responses from the L-Connect service on localhost.
- GetTemplates and GetSelectedTemplateId responses for detected devices.
- ReloadAssets responses.
- Template/profile/uploaded file snapshots before and after the operation.
- Changed L-Connect files during the trace window.
- Recent L-Connect logs and profile files.

Limitations
This tool actively probes L-Connect's local HTTP service. It does not install a packet capture driver and does not intercept private process memory.

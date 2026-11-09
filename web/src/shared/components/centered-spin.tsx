import { Spin } from 'antd';
import type { SpinProps } from 'antd';

interface CenteredSpinProps {
  tip?: React.ReactNode; // loading label — already translated at the call site
  style?: React.CSSProperties; // outer positioning (margins, display) preserved from prior standalone usages
  minHeight?: number | string; // placeholder area so the nested overlay has room to center the spinner + tip
  size?: SpinProps['size']; // forwarded to Spin (e.g. "large" for route-chunk fallbacks)
}

// CenteredSpin — loading indicator with a label using antd's nest pattern.
// A standalone <Spin tip=.../> warns ("`tip` only work in nest or fullscreen pattern")
// and drops the label; nesting an empty placeholder makes `tip` valid and renders it.
// Positioning style lives on a plain wrapper so layout is independent of antd internals.
export function CenteredSpin({ tip, style, minHeight = 64, size }: CenteredSpinProps) {
  return (
    <div style={style}>
      <Spin tip={tip} size={size}>
        <div style={{ minHeight }} />
      </Spin>
    </div>
  );
}

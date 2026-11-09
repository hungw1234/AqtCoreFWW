// signature-widget.tsx — canvas-based signature capture with clear + base64 output
// Fix A1: responsive height (3:1 aspect ratio, 100–300px range) + DPR scaling on both restore paths
import { useRef, useEffect, useCallback, useState } from 'react';
import { Button, Space } from 'antd';
import { useTranslation } from 'react-i18next';
import SignatureCanvas from 'react-signature-canvas';

interface SignatureWidgetProps {
  value?: string;
  onChange?: (base64: string) => void;
  disabled?: boolean;
}

// Compute canvas height from width using 3:1 aspect ratio.
// Floor: 100px (very narrow containers), cap: 300px (prevents excessive height on wide desktop).
function computeHeight(width: number): number {
  return Math.min(Math.max(Math.round(width / 3), 100), 300);
}

export function SignatureWidget({ value, onChange, disabled }: SignatureWidgetProps) {
  const { t } = useTranslation();
  const sigRef = useRef<SignatureCanvas>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const [canvasWidth, setCanvasWidth] = useState(400);
  const [canvasHeight, setCanvasHeight] = useState(133); // 400/3

  const canvasWidthRef = useRef(400);
  const canvasHeightRef = useRef(133);

  // Measure container width for responsive canvas resolution.
  // On resize: saves current drawing, updates dimensions, restores after re-render.
  // Both refs track values synchronously for the rAF closure (state is stale in closures).
  useEffect(() => {
    if (!containerRef.current) return;
    const measure = () => {
      const w = containerRef.current?.clientWidth ?? 400;
      const newWidth = Math.max(w - 10, 200);
      if (newWidth === canvasWidthRef.current) return; // Skip if unchanged

      const newHeight = computeHeight(newWidth);

      // Save current drawing before resize triggers re-render
      let savedData: string | null = null;
      if (sigRef.current && !sigRef.current.isEmpty()) {
        savedData = sigRef.current.toDataURL('image/png');
      }

      canvasWidthRef.current = newWidth;
      canvasHeightRef.current = newHeight;
      setCanvasWidth(newWidth);
      setCanvasHeight(newHeight);

      // Restore after React re-render; pass DPR for correct Retina scaling
      if (savedData) {
        requestAnimationFrame(() => {
          sigRef.current?.fromDataURL(savedData!, {
            width: newWidth,
            height: newHeight,
            ratio: window.devicePixelRatio || 1,
          });
        });
      }
    };
    measure();
    const observer = new ResizeObserver(measure);
    observer.observe(containerRef.current);
    return () => observer.disconnect();
  }, []);

  // Restore saved signature when value changes externally.
  // Passes current canvas dimensions and DPR to prevent Retina blur.
  // isEmpty() guard prevents overwriting an active drawing when canvas resizes.
  useEffect(() => {
    if (!sigRef.current) return;
    if (value && sigRef.current.isEmpty()) {
      sigRef.current.fromDataURL(value, {
        width: canvasWidth,
        height: canvasHeight,
        ratio: window.devicePixelRatio || 1,
      });
    }
  }, [value, canvasWidth, canvasHeight]);

  const handleEnd = useCallback(() => {
    if (!sigRef.current || disabled) return;
    const dataUrl = sigRef.current.toDataURL('image/png');
    // Guard: reject signatures > 500KB base64 (canvas area capped at ~900×300 → well under limit)
    if (dataUrl.length > 500_000) {
      sigRef.current.clear();
      onChange?.('');
      return;
    }
    onChange?.(dataUrl);
  }, [onChange, disabled]);

  const handleClear = useCallback(() => {
    if (!sigRef.current) return;
    sigRef.current.clear();
    onChange?.('');
  }, [onChange]);

  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <div
        ref={containerRef}
        style={{
          border: '1px solid #d9d9d9',
          borderRadius: 4,
          padding: 4,
          touchAction: 'none', // Prevent scroll conflict on touch devices
          background: disabled ? '#f5f5f5' : '#fff',
        }}
      >
        <SignatureCanvas
          ref={sigRef}
          penColor="black"
          canvasProps={{
            width: canvasWidth,
            height: canvasHeight,
            style: { width: '100%', height: canvasHeight },
          }}
          onEnd={handleEnd}
        />
      </div>
      {!disabled && (
        <Button size="small" onClick={handleClear}>
          {t('forms.signature.clear')}
        </Button>
      )}
    </Space>
  );
}

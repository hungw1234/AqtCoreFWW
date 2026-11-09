import { Alert } from 'antd';

// Banner thông báo tính năng AI đang ở trạng thái scaffold (interface + stub).
// Hiển thị trên các trang AI chưa wire pipeline end-to-end.
// Sẽ được gỡ bỏ khi tính năng được implement thực tế theo business case cụ thể.
// Xem: docs/system-architecture.md — "AI Module — Implementation Strategy"
export function DeferredFeatureAlert({ feature }: { feature: string }) {
  return (
    <Alert
      type="info"
      showIcon
      closable
      style={{ marginBottom: 16 }}
      message={`${feature} — tính năng đang ở trạng thái scaffold`}
      description={
        <>
          <div>
            Module AI hiện được giữ ở mức <strong>interface + stub</strong> theo chiến lược
            deferred implementation. Khi có use case nghiệp vụ cụ thể, các adapter thực tế
            (LLM, QueryCatalog executor, RAG pipeline) sẽ được kích hoạt.
          </div>
          <div style={{ marginTop: 4, fontSize: 12, opacity: 0.85 }}>
            AI module kept as interface + stub until a concrete business case arrives.
          </div>
        </>
      }
    />
  );
}

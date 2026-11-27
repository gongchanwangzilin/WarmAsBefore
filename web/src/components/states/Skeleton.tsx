type SkeletonProps = {
  w?: string;
  h?: string;
  r?: string;
  className?: string;
};

/** 骨架块：载入时把版面的形状先占出来，不是转圈。 */
export function Skeleton({ w = '100%', h = '14px', r = '6px', className = '' }: SkeletonProps) {
  return (
    <span
      className={`skeleton ${className}`.trim()}
      style={{ '--sk-w': w, '--sk-h': h, '--sk-r': r } as React.CSSProperties}
      aria-hidden="true"
    />
  );
}

export function SkeletonRows({ count = 3 }: { count?: number }) {
  return (
    <div className="sk-rows" aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <div className="sk-row" key={index}>
          <Skeleton w="38%" h="13px" />
          <Skeleton w="18%" h="12px" />
        </div>
      ))}
    </div>
  );
}

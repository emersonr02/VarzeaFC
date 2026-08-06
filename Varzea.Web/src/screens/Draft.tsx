import type { Attr, LegendOption } from "../api/types";
import { ATTR_LABEL, ATTR_ORDER, ATTR_SHORT, initials } from "../data/labels";

interface Props {
  round: number; // 1-8
  attribute: Attr;
  candidates: LegendOption[];
  dock: Partial<Record<Attr, number>>;
  onPick: (index: number) => void;
  onExit: () => void;
  loading: boolean;
  error: string | null;
}

export function Draft({ round, attribute, candidates, dock, onPick, onExit, loading, error }: Props) {
  return (
    <section className="screen pitch-bg">
      <div className="wrap">
        <div className="top-nav">
          <button className="back-btn" onClick={onExit}>✕ Sair</button>
          <span className="step-label">Passo 2 de 3 · Rodada {round} de 8</span>
        </div>
        <div className="progress-track">
          <div className="progress-fill" style={{ width: `${(round - 1) * 12.5 + 6}%` }} />
        </div>
        {error && <div className="error-banner">{error}</div>}
        <div className="attr-banner">
          <div className="lbl">Rodada de roubo — escolha de quem herdar</div>
          <div className="big">{ATTR_LABEL[attribute]}</div>
        </div>
        <div className="legend-row">
          {candidates.map((leg, i) => (
            <div
              key={i}
              className="legend-card"
              style={loading ? { pointerEvents: "none", opacity: 0.6 } : undefined}
              onClick={() => !loading && onPick(i)}
            >
              <div className="legend-photo">{initials(leg.name)}</div>
              <div className="legend-name">{leg.name}</div>
              <div className="legend-val">
                {leg.rating}
                <span className="sm">{ATTR_LABEL[attribute]}</span>
              </div>
            </div>
          ))}
        </div>
        <div className="mini-card-dock">
          <div className="dock-title">Seus atributos roubados</div>
          <div className="dock-grid">
            {ATTR_ORDER.map((a) => {
              const has = dock[a] !== undefined;
              return (
                <div className="dock-slot" key={a} style={has ? { background: "rgba(243,195,50,.12)" } : undefined}>
                  <div className="dock-k">{ATTR_SHORT[a]}</div>
                  <div className={`dock-v ${has ? "" : "empty"}`}>{has ? dock[a] : "—"}</div>
                </div>
              );
            })}
          </div>
        </div>
      </div>
    </section>
  );
}

import type { Pos, PositionPotential } from "../api/types";
import { POS_LABEL } from "../data/labels";

interface Props {
  potentials: PositionPotential[];
  onChoose: (pos: Pos) => void;
  onExit: () => void;
  loading: boolean;
  error: string | null;
}

export function PositionSelect({ potentials, onChoose, onExit, loading, error }: Props) {
  const rows = [...potentials].sort((a, b) => b.potential - a.potential);
  return (
    <section className="screen pitch-bg">
      <div className="wrap">
        <div className="top-nav">
          <button className="back-btn" onClick={onExit}>✕ Sair</button>
          <span className="step-label">Passo 3 de 3 · Posição</span>
        </div>
        <h2 className="h2">Escolha sua <span className="accent">posição</span></h2>
        <p className="pos-hint">Cada posição usa seus atributos de um jeito diferente — este número é o seu <b>potencial</b> nela.</p>
        {error && <div className="error-banner">{error}</div>}
        <div className="pos-list">
          {rows.map((r) => (
            <div
              key={r.position}
              className="pos-row"
              style={loading ? { pointerEvents: "none", opacity: 0.6 } : undefined}
              onClick={() => !loading && onChoose(r.position)}
            >
              <div className="pos-left">
                <div className="pos-name">{POS_LABEL[r.position]}</div>
                <div className="pos-sub">{r.position}</div>
              </div>
              <div className="pos-ovr">{r.potential}</div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

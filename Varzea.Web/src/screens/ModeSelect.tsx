export type Pace = "season" | "match";

interface Props {
  onChoose: (pace: Pace) => void;
  onBack: () => void;
}

export function ModeSelect({ onChoose, onBack }: Props) {
  return (
    <section className="screen pitch-bg">
      <div className="wrap">
        <div className="top-nav">
          <button className="back-btn" onClick={onBack}>← Voltar</button>
          <span className="step-label">Como quer jogar?</span>
        </div>
        <h2 className="h2">Escolha o <span className="accent">ritmo</span></h2>
        <div className="mode-grid">
          <div className="mode-card" onClick={() => onChoose("match")}>
            <span className="mode-tag">Detalhado</span>
            <div className="mode-title">Jogo a Jogo</div>
            <div className="mode-desc">Avance partida por partida da temporada, veja placares e seus números.</div>
          </div>
          <div className="mode-card" onClick={() => onChoose("season")}>
            <span className="mode-tag">Rápido</span>
            <div className="mode-title">Temporada a Temporada</div>
            <div className="mode-desc">Avance ano a ano: cada clique resolve a temporada inteira — liga, copas, seleção e mercado.</div>
          </div>
        </div>
      </div>
    </section>
  );
}

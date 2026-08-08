interface Props {
  country: string;
  options: string[];
  onChoose: (index: number) => void;
  onExit: () => void;
  loading: boolean;
  error: string | null;
}

// Roadmap pós-§9, painel Clube: 3 clubes REAIS do país escolhido, vindos de
// /careers/clubs (ClubDirectory, clubs.json) — nada de nome gerado no front.
export function ClubSelect({ country, options, onChoose, onExit, loading, error }: Props) {
  return (
    <section className="screen pitch-bg">
      <div className="wrap">
        <div className="top-nav">
          <button className="back-btn" onClick={onExit}>✕ Sair</button>
          <span className="step-label">Escolha seu clube</span>
        </div>
        <h2 className="h2">Qual clube de <span className="accent">{country}</span> te dá a chance?</h2>
        <p className="pos-hint">Três clubes reais te querem — escolha onde começa sua carreira.</p>
        {error && <div className="error-banner">{error}</div>}
        <div className="pos-list">
          {options.map((name, i) => (
            <div
              key={name}
              className="pos-row"
              style={loading ? { pointerEvents: "none", opacity: 0.6 } : undefined}
              onClick={() => !loading && onChoose(i)}
            >
              <div className="pos-left">
                <div className="pos-name">{name}</div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

interface Props {
  nickname: string;
  onNicknameChange: (v: string) => void;
  country: string;
  onCountryChange: (v: string) => void;
  countries: string[];
  onBack: () => void;
  onSubmit: () => void;
  loading: boolean;
  error: string | null;
}

export function Setup({ nickname, onNicknameChange, country, onCountryChange, countries, onBack, onSubmit, loading, error }: Props) {
  return (
    <section className="screen pitch-bg">
      <div className="wrap">
        <div className="top-nav">
          <button className="back-btn" onClick={onBack}>← Voltar</button>
          <span className="step-label">Passo 1 de 3 · Identidade</span>
        </div>
        <h2 className="h2">Quem é <span className="accent">você</span>?</h2>
        <div className="card">
          <div className="tape" />
          {error && <div className="error-banner">{error}</div>}
          <div className="field">
            <label>Apelido do jogador</label>
            <input
              type="text"
              maxLength={22}
              placeholder="Ex: Zezinho da Vila"
              value={nickname}
              onChange={(e) => onNicknameChange(e.target.value)}
            />
          </div>
          <div className="field">
            <label>Nacionalidade (define seu clube de base)</label>
            <div className="chip-row">
              {countries.map((c) => (
                <div key={c} className={`chip ${c === country ? "sel" : ""}`} onClick={() => onCountryChange(c)}>
                  {c}
                </div>
              ))}
            </div>
          </div>
          <button className="btn rust block" disabled={loading || !country} onClick={onSubmit}>
            {loading ? "Carregando…" : "Começar o Draft →"}
          </button>
        </div>
      </div>
    </section>
  );
}

import { useEffect, useState } from "react";
import { api, isDraftComplete } from "./api/client";
import type { Attr, LegendOption, Pos, PositionPotential } from "./api/types";
import { Album } from "./screens/Album";
import { Draft } from "./screens/Draft";
import { Home } from "./screens/Home";
import { ModeSelect } from "./screens/ModeSelect";
import { PositionSelect } from "./screens/PositionSelect";
import { Result } from "./screens/Result";
import { Reveal } from "./screens/Reveal";
import { Setup } from "./screens/Setup";
import { Sim } from "./screens/Sim";

type Screen = "home" | "setup" | "draft" | "position" | "reveal" | "mode" | "sim" | "result" | "album";

export default function App() {
  const [screen, setScreen] = useState<Screen>("home");
  const [countries, setCountries] = useState<string[]>([]);
  const [metaError, setMetaError] = useState<string | null>(null);

  const [nickname, setNickname] = useState("");
  const [country, setCountry] = useState("");
  const [token, setToken] = useState<string | null>(null);

  const [round, setRound] = useState(1);
  const [attribute, setAttribute] = useState<Attr>("Pac");
  const [candidates, setCandidates] = useState<LegendOption[]>([]);
  const [dock, setDock] = useState<Partial<Record<Attr, number>>>({});

  const [attrs, setAttrs] = useState<number[]>([]);
  const [potentials, setPotentials] = useState<PositionPotential[]>([]);
  const [position, setPosition] = useState<Pos | null>(null);
  const [potential, setPotential] = useState(0);
  const [role, setRole] = useState("");

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .meta()
      .then((m) => {
        setCountries(m.countries);
        setCountry(m.countries[0] ?? "");
      })
      .catch((e) => setMetaError(e instanceof Error ? e.message : "Não foi possível carregar os dados do jogo."));
  }, []);

  function resetCareer() {
    setToken(null);
    setRound(1);
    setDock({});
    setAttrs([]);
    setPotentials([]);
    setPosition(null);
    setPotential(0);
    setRole("");
    setError(null);
  }

  function goSetup() {
    resetCareer();
    setScreen("setup");
  }

  async function submitSetup() {
    setLoading(true);
    setError(null);
    try {
      const resp = await api.start();
      setToken(resp.token);
      setRound(resp.round);
      setAttribute(resp.attribute);
      setCandidates(resp.candidates);
      setDock({});
      setScreen("draft");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Não foi possível começar o draft.");
    } finally {
      setLoading(false);
    }
  }

  async function pickDraft(index: number) {
    if (!token) return;
    const picked = candidates[index];
    setLoading(true);
    setError(null);
    try {
      const resp = await api.draft(token, index);
      setDock((d) => ({ ...d, [attribute]: picked.rating }));
      if (isDraftComplete(resp)) {
        setAttrs(resp.attributes);
        setPotentials(resp.potentials);
        setToken(resp.token);
        setScreen("position");
      } else {
        setToken(resp.token);
        setRound(resp.round);
        setAttribute(resp.attribute);
        setCandidates(resp.candidates);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Não foi possível confirmar a escolha.");
    } finally {
      setLoading(false);
    }
  }

  async function choosePosition(pos: Pos) {
    if (!token) return;
    setLoading(true);
    setError(null);
    try {
      const resp = await api.position(token, pos, country);
      setPosition(pos);
      setPotential(resp.potential);
      setRole(resp.role);
      setToken(resp.token);
      setScreen("reveal");
    } catch (e) {
      setError(e instanceof Error ? e.message : "Não foi possível travar a posição.");
    } finally {
      setLoading(false);
    }
  }

  if (metaError) {
    return (
      <div id="app">
        <div className="grain" />
        <section className="screen pitch-bg">
          <div className="wrap">
            <div className="error-banner">
              {metaError} — confirme que o Varzea.Api está rodando em http://localhost:52525.
            </div>
          </div>
        </section>
      </div>
    );
  }

  return (
    <div id="app">
      <div className="grain" />
      {screen === "home" && <Home onPlay={goSetup} onAlbum={() => setScreen("album")} />}

      {screen === "setup" && (
        <Setup
          nickname={nickname}
          onNicknameChange={setNickname}
          country={country}
          onCountryChange={setCountry}
          countries={countries}
          onBack={() => setScreen("home")}
          onSubmit={submitSetup}
          loading={loading}
          error={error}
        />
      )}

      {screen === "draft" && (
        <Draft
          round={round}
          attribute={attribute}
          candidates={candidates}
          dock={dock}
          onPick={pickDraft}
          onExit={() => setScreen("home")}
          loading={loading}
          error={error}
        />
      )}

      {screen === "position" && (
        <PositionSelect
          potentials={potentials}
          onChoose={choosePosition}
          onExit={() => setScreen("home")}
          loading={loading}
          error={error}
        />
      )}

      {screen === "reveal" && position && (
        <Reveal
          nickname={nickname || "Craque Sem Nome"}
          country={country}
          position={position}
          potential={potential}
          role={role}
          attrs={attrs}
          onContinue={() => setScreen("mode")}
          onRestart={goSetup}
        />
      )}

      {screen === "mode" && (
        <ModeSelect onChooseSeasonBySeason={() => setScreen("sim")} onBack={() => setScreen("reveal")} />
      )}

      {screen === "sim" && position && token && (
        <Sim
          nickname={nickname || "Craque Sem Nome"}
          country={country}
          position={position}
          role={role}
          potential={potential}
          initialToken={token}
          onExit={() => setScreen("home")}
          onFinished={(finalToken) => {
            setToken(finalToken);
            setScreen("result");
          }}
        />
      )}

      {screen === "result" && position && token && (
        <Result
          token={token}
          nickname={nickname || "Craque Sem Nome"}
          country={country}
          position={position}
          potential={potential}
          role={role}
          attrs={attrs}
          onSaveComplete={() => {}}
          onNewCareer={goSetup}
          onHome={() => setScreen("home")}
        />
      )}

      {screen === "album" && <Album onBack={() => setScreen("home")} />}
    </div>
  );
}

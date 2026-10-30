"""OpenAI-compatible provider factory (LLM_PROVIDER=real)."""

from typing import Any, Protocol

from pydantic import BaseModel, SecretStr


class StructuredLlm(Protocol):
    def with_structured_output(self, schema: type[BaseModel]) -> Any: ...


def build_real_llm(
    base_url: str,
    api_key: str,
    model: str,
    temperature: float = 0.0,
    timeout_seconds: float = 60.0,
    max_retries: int = 2,
) -> StructuredLlm:
    if not api_key:
        raise ValueError(
            "LLM_API_KEY is required when LLM_PROVIDER=real. "
            "Set it via environment variable or .env file."
        )

    from langchain_openai import ChatOpenAI

    return ChatOpenAI(
        model=model,
        api_key=SecretStr(api_key),
        base_url=base_url,
        temperature=temperature,
        timeout=timeout_seconds,
        max_retries=max_retries,
        default_headers={"HTTP-Referer": "https://financialai.local"},
    )


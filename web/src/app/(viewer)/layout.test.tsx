import { afterEach, describe, expect, it } from "vitest";
import { cleanup, render } from "@testing-library/react";
import ViewerLayout from "./layout";

afterEach(cleanup);

describe("ViewerLayout", () => {
  it("wraps viewer pages in the dark brand shell", () => {
    const { container } = render(
      <ViewerLayout params={Promise.resolve({})}>
        <p>page</p>
      </ViewerLayout>,
    );

    const shell = container.querySelector(".viewer");
    expect(shell?.textContent).toBe("page");
  });

  it("decides between the full intro and the short fade before the page paints", () => {
    const { container } = render(
      <ViewerLayout params={Promise.resolve({})}>
        <p>page</p>
      </ViewerLayout>,
    );

    const script = container.querySelector("script");
    expect(script?.textContent).toContain("data-ss-intro");
    expect(container.firstElementChild).toBe(script);
  });
});

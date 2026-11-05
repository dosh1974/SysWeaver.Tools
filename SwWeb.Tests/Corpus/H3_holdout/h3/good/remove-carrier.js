/*
 * Carriers page: remove a parcel carrier account. Every warehouse user loses the carrier and its stored key is deleted for good, so the
 * confirmation names the carrier and the consequence and asks to type its name (UX guide SAF-1, SAF-2, SAF-4).
 */
(function () {
    "use strict";

    /**
     * The Remove button of one carrier account.
     * @param {{ Id: number, Name: string }} carrier The carrier account.
     * @param {function(): void} onRemoved Called after the server removed it (the page drops its card).
     * @returns {Sw.Button} The button.
     */
    function RemoveCarrierButton(carrier, onRemoved) {
        const button = new Sw.Button({
            Text: _TF("Remove", "Button on the card of a parcel carrier account: removes the account (asks first)"),
            AriaLabel: _T("Remove {0}", carrier.Name, "Accessible name of the Remove button of a carrier account. {0}: the carrier name, ex: DHL Express"),
            Title: _TF("Remove the carrier account for every warehouse user (asks first)", "Tool tip of the Remove button of a carrier account"),
            Kind: "danger",
            OnClick: () => {
                Sw.Danger.Run({
                    Action: _TF("Remove", "Verb of the confirmation that removes a carrier account; also its danger button"),
                    Target: carrier.Name,
                    Consequence: _TF("Return labels can no longer be printed with this carrier, and its stored API key is deleted.", "Text of the confirmation before a carrier account is removed"),
                    Confirm: carrier.Name,
                    Button: button,
                    Run: async () => {
                        await Sw.Net.Send("../Api/carriers/Remove", { Id: carrier.Id });
                        onRemoved();
                    },
                    Done: _T("The carrier {0} was removed.", carrier.Name, "Result after a carrier account was removed. {0}: the carrier name, ex: DHL Express"),
                });
            },
        });
        return button;
    }

    window.Carriers = { RemoveCarrierButton };
})();

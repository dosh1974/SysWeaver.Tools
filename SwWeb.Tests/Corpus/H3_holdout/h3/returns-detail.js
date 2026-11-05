/*
 * Warehouse returns: the detail dialog of one return (facts, the customer's message, the warehouse note, remove).
 */
(function () {
    "use strict";

    const api = window.ReturnsApi;
    const noteLimit = 500;

    class ReturnDetail {
        static Open(id, o) {
            return new ReturnDetail(id, o).show();
        }

        constructor(id, o) {
            this.id = id;
            this.o = o || {};
            this.r = null;
        }

        changed() {
            if (this.o.OnChanged)
                this.o.OnChanged();
        }

        async claim() {
            try {
                await Sw.Net.Send("../Api/returns/Claim", { Id: this.id });
            } catch (e) {
                console.warn("Claim failed", e);
            }
        }

        async show() {
            try {
                this.r = await api.Get(this.id);
            } catch (e) {
                if (Sw.Net.ShouldReport(e))
                    Sw.Fail(Sw.Net.FailText(_TF("Open return", "Name of the action in the failure message when a return could not be opened"), e));
                return null;
            }
            await this.claim();
            const { Rma, Customer, CustomerMessage, Created, Refund, Currency, Note } = this.r;

            const facts = Sw.Facts({
                Card: false,
                Items: [
                    { Label: _TF("Customer", "Label of the customer name in the return details"), Value: Customer },
                    { Label: _TF("Registered", "Label of the time the return was registered"), Value: Sw.Format.TimeStamp(Created)[1], Title: Sw.Format.TimeStamp(Created)[0] },
                    { Label: _TF("Refund", "Label of the refund amount of the return"), Value: Sw.Format.Amount(Currency, Refund) },
                ],
            });

            const message = Sw.el("div.ret-message.sw-selectable");
            message.innerHTML = CustomerMessage || "";

            const note = new Sw.TextBox({
                Label: _TF("Warehouse note", "Label of the note the warehouse keeps on a return"),
                Hint: _T("At most {0} characters.", Sw.Format.Number(noteLimit), "Hint under the warehouse note. {0}: the limit, ex: 500"),
                Multiline: true,
                Rows: 4,
                Value: Note || "",
                OnInput: v => note.SetError(v.length > noteLimit
                    ? _T("The note is longer than {0} characters.", Sw.Format.Number(noteLimit), "Error under the warehouse note. {0}: the limit, ex: 500")
                    : null),
            });

            return Sw.Dialog.Show({
                Title: _T("Return {0}", Rma, "Title of the return details dialog. {0}: the return number, ex: RMA-10442"),
                Id: "returns.detail",
                Content: Sw.el("div", null, facts.E, message, note.E),
                Buttons: [
                    {
                        Text: _TF("Cancel", "Button that closes the return details without saving"),
                        Title: _TF("Close the details without saving the note", "Tool tip of Cancel in the return details"),
                        Value: null,
                    },
                    {
                        Text: _TF("Remove", "Button in the return details: removes the return from the warehouse list"),
                        Title: _TF("Remove this return from the warehouse list", "Tool tip of the Remove button in the return details"),
                        Kind: "danger",
                        Value: "removed",
                        OnClick: () => this.remove(),
                    },
                    {
                        Text: _TF("Save note", "Button that saves the warehouse note of the return"),
                        Title: _T("Save the warehouse note of {0}", Rma, "Tool tip of Save note. {0}: the return number, ex: RMA-10442"),
                        Kind: "primary",
                        Value: "saved",
                        OnClick: () => this.saveNote(note.Get()),
                    },
                ],
            });
        }

        async remove() {
            await api.Remove(this.id);
            this.changed();
        }

        async saveNote(text) {
            if (this.r.Closed)
                throw new Error("This return is closed, so its note can no longer be changed.");
            await api.SaveNote(this.id, text);
            this.changed();
        }
    }

    window.ReturnDetail = ReturnDetail;
})();

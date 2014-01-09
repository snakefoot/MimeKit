//
// InternetAddress.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2013 Jeffrey Stedfast
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.Text;
using System.Collections.Generic;

using MimeKit.Utils;

namespace MimeKit {
	/// <summary>
	/// An internet address, as specified by rfc0822.
	/// </summary>
	public abstract class InternetAddress
	{
		Encoding encoding;
		string name;

		/// <summary>
		/// Initializes a new instance of the <see cref="MimeKit.InternetAddress"/> class.
		/// </summary>
		/// <param name="encoding">The character encoding to be used for encoding the name.</param>
		/// <param name="name">The name of the mailbox or group.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="encoding"/> is <c>null</c>.
		/// </exception>
		protected InternetAddress (Encoding encoding, string name)
		{
			if (encoding == null)
				throw new ArgumentNullException ("encoding");

			Encoding = encoding;
			Name = name;
		}

		/// <summary>
		/// Gets or sets the character encoding to use when encoding the name of the address.
		/// </summary>
		/// <value>The character encoding.</value>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="value"/> is <c>null</c>.
		/// </exception>
		public Encoding Encoding {
			get { return encoding; }
			set {
				if (value == null)
					throw new ArgumentNullException ("value");

				if (value == encoding)
					return;

				encoding = value;
				OnChanged ();
			}
		}

		/// <summary>
		/// Gets or sets the name of the address.
		/// </summary>
		/// <value>The name of the address.</value>
		public string Name {
			get { return name; }
			set {
				if (value == name)
					return;

				name = value;
				OnChanged ();
			}
		}

		internal abstract void Encode (FormatOptions options, StringBuilder builder, ref int lineLength);

		/// <summary>
		/// Serializes the <see cref="MimeKit.InternetAddress"/> to a string, optionally encoding it for transport.
		/// </summary>
		/// <returns>A string representing the <see cref="MimeKit.InternetAddress"/>.</returns>
		/// <param name="encode">If set to <c>true</c>, the <see cref="MimeKit.InternetAddress"/> will be encoded.</param>
		public abstract string ToString (bool encode);

		/// <summary>
		/// Serializes the <see cref="MimeKit.InternetAddress"/> to a string suitable for display.
		/// </summary>
		/// <returns>A string representing the <see cref="MimeKit.InternetAddress"/>.</returns>
		public override string ToString ()
		{
			return ToString (false);
		}

		internal event EventHandler Changed;

		/// <summary>
		/// Raises the internal changed event used by <see cref="MimeKit.MimeMessage"/> to keep headers in sync.
		/// </summary>
		protected virtual void OnChanged ()
		{
			if (Changed != null)
				Changed (this, EventArgs.Empty);
		}

		static bool TryParseLocalPart (byte[] text, ref int index, int endIndex, bool throwOnError, out string localpart)
		{
			StringBuilder token = new StringBuilder ();
			int startIndex = index;

			localpart = null;

			do {
				if (!text[index].IsAtom () && text[index] != '"') {
					if (throwOnError)
						throw new ParseException (string.Format ("Invalid local-part at offset {0}", startIndex), startIndex, index);

					return false;
				}

				int start = index;
				if (!ParseUtils.SkipWord (text, ref index, endIndex, throwOnError))
					return false;

				token.Append (Encoding.ASCII.GetString (text, start, index - start));

				if (!ParseUtils.SkipCommentsAndWhiteSpace (text, ref index, endIndex, throwOnError))
					return false;

				if (index >= endIndex || text[index] != (byte) '.')
					break;

				token.Append ('.');
				index++;

				if (!ParseUtils.SkipCommentsAndWhiteSpace (text, ref index, endIndex, throwOnError))
					return false;

				if (index >= endIndex) {
					if (throwOnError)
						throw new ParseException (string.Format ("Incomplete local-part at offset {0}", startIndex), startIndex, index);

					return false;
				}
			} while (true);

			localpart = token.ToString ();

			return true;
		}

		static bool TryParseAddrspec (byte[] text, ref int index, int endIndex, byte sentinel, bool throwOnError, out string addrspec)
		{
			int startIndex = index;

			addrspec = null;

			string localpart;
			if (!TryParseLocalPart (text, ref index, endIndex, throwOnError, out localpart))
				return false;

			if (index >= endIndex || text[index] == sentinel) {
				addrspec = localpart;
				return true;
			}

			if (text[index] != (byte) '@') {
				if (throwOnError)
					throw new ParseException (string.Format ("Invalid addr-spec token at offset {0}", startIndex), startIndex, index);

				return false;
			}

			index++;
			if (index >= endIndex) {
				if (throwOnError)
					throw new ParseException (string.Format ("Incomplete addr-spec token at offset {0}", startIndex), startIndex, index);

				return false;
			}

			if (!ParseUtils.SkipCommentsAndWhiteSpace (text, ref index, endIndex, throwOnError))
				return false;

			if (index >= endIndex) {
				if (throwOnError)
					throw new ParseException (string.Format ("Incomplete addr-spec token at offset {0}", startIndex), startIndex, index);

				return false;
			}

			string domain;
			if (!ParseUtils.TryParseDomain (text, ref index, endIndex, throwOnError, out domain))
				return false;

			addrspec = localpart + "@" + domain;

			return true;
		}

		internal static bool TryParseMailbox (byte[] text, int startIndex, ref int index, int endIndex, string name, int codepage, bool throwOnError, out InternetAddress address)
		{
			Encoding encoding = Encoding.GetEncoding (codepage);
			DomainList route = null;

			address = null;

			// skip over the '<'
			index++;
			if (index >= endIndex) {
				if (throwOnError)
					throw new ParseException (string.Format ("Incomplete mailbox at offset {0}", startIndex), startIndex, index);

				return false;
			}

			if (text[index] == (byte) '@') {
				if (!DomainList.TryParse (text, ref index, endIndex, throwOnError, out route)) {
					if (throwOnError)
						throw new ParseException (string.Format ("Invalid route in mailbox at offset {0}", startIndex), startIndex, index);

					return false;
				}

				if (index + 1 >= endIndex || text[index] != (byte) ':') {
					if (throwOnError)
						throw new ParseException (string.Format ("Incomplete route in mailbox at offset {0}", startIndex), startIndex, index);

					return false;
				}

				index++;
			}

			string addrspec;
			if (!TryParseAddrspec (text, ref index, endIndex, (byte) '>', throwOnError, out addrspec))
				return false;

			if (!ParseUtils.SkipCommentsAndWhiteSpace (text, ref index, endIndex, throwOnError))
				return false;

			if (index >= endIndex || text[index] != (byte) '>') {
				if (throwOnError)
					throw new ParseException (string.Format ("Unexpected end of mailbox at offset {0}", startIndex), startIndex, index);

				return false;
			}

			if (route != null)
				address = new MailboxAddress (encoding, name, route, addrspec);
			else
				address = new MailboxAddress (encoding, name, addrspec);

			index++;

			return true;
		}

		static bool TryParseGroup (ParserOptions options, byte[] text, int startIndex, ref int index, int endIndex, string name, int codepage, bool throwOnError, out InternetAddress address)
		{
			Encoding encoding = Encoding.GetEncoding (codepage);
			List<InternetAddress> members;

			address = null;

			// skip over the ':'
			index++;
			if (index >= endIndex) {
				if (throwOnError)
					throw new ParseException (string.Format ("Incomplete address group at offset {0}", startIndex), startIndex, index);

				return false;
			}

			if (InternetAddressList.TryParse (options, text, ref index, endIndex, true, throwOnError, out members))
				address = new GroupAddress (encoding, name, members);
			else
				address = new GroupAddress (encoding, name);

			if (index >= endIndex || text[index] != (byte) ';') {
				if (throwOnError)
					throw new ParseException (string.Format ("Expected to find ';' at offset {0}", index), startIndex, index);

				while (index < endIndex && text[index] != (byte) ';')
					index++;
			} else {
				index++;
			}

			return true;
		}

		internal static bool TryParse (ParserOptions options, byte[] text, ref int index, int endIndex, bool throwOnError, out InternetAddress address)
		{
			address = null;

			if (!ParseUtils.SkipCommentsAndWhiteSpace (text, ref index, endIndex, throwOnError))
				return false;

			// keep track of the start & length of the phrase
			int startIndex = index;
			int length = 0;

			while (index < endIndex && ParseUtils.Skip8bitWord (text, ref index, endIndex, throwOnError)) {
				length = index - startIndex;

				do {
					if (!ParseUtils.SkipCommentsAndWhiteSpace (text, ref index, endIndex, throwOnError))
						return false;

					// Note: some clients don't quote dots in the name
					if (index >= endIndex || text[index] != (byte) '.')
						break;

					index++;
				} while (true);
			}

			if (!ParseUtils.SkipCommentsAndWhiteSpace (text, ref index, endIndex, throwOnError))
				return false;

			// specials    =  "(" / ")" / "<" / ">" / "@"  ; Must be in quoted-
			//             /  "," / ";" / ":" / "\" / <">  ;  string, to use
			//             /  "." / "[" / "]"              ;  within a word.

			if (index >= endIndex || text[index] == (byte) ',' || text[index] == ';') {
				// we've completely gobbled up an addr-spec w/o a domain
				byte sentinel = index < endIndex ? text[index] : (byte) ',';
				string name, addrspec;

				// rewind back to the beginning of the local-part
				index = startIndex;

				if (!TryParseAddrspec (text, ref index, endIndex, sentinel, throwOnError, out addrspec))
					return false;

				ParseUtils.SkipWhiteSpace (text, ref index, endIndex);

				if (index < endIndex && text[index] == '(') {
					int comment = index;

					if (!ParseUtils.SkipComment (text, ref index, endIndex)) {
						if (throwOnError)
							throw new ParseException (string.Format ("Incomplete comment token at offset {0}", comment), comment, index);

						return false;
					}

					comment++;

					name = Rfc2047.DecodePhrase (options, text, comment, (index - 1) - comment).Trim ();
				} else {
					name = string.Empty;
				}

				address = new MailboxAddress (name, addrspec);

				return true;
			}

			if (text[index] == (byte) ':') {
				// rfc2822 group address
				int codepage;
				string name;

				if (length > 0) {
					name = Rfc2047.DecodePhrase (options, text, startIndex, length, out codepage);
				} else {
					name = string.Empty;
					codepage = 65001;
				}

				return TryParseGroup (options, text, startIndex, ref index, endIndex, MimeUtils.Unquote (name), codepage, throwOnError, out address);
			}

			if (text[index] == (byte) '<') {
				// rfc2822 angle-addr token
				int codepage;
				string name;

				if (length > 0) {
					name = Rfc2047.DecodePhrase (options, text, startIndex, length, out codepage);
				} else {
					name = string.Empty;
					codepage = 65001;
				}

				return TryParseMailbox (text, startIndex, ref index, endIndex, MimeUtils.Unquote (name), codepage, throwOnError, out address);
			}

			if (text[index] == (byte) '@') {
				// we're either in the middle of an addr-spec token or we completely gobbled up an addr-spec w/o a domain
				string name, addrspec;

				// rewind back to the beginning of the local-part
				index = startIndex;

				if (!TryParseAddrspec (text, ref index, endIndex, (byte) ',', throwOnError, out addrspec))
					return false;

				ParseUtils.SkipWhiteSpace (text, ref index, endIndex);

				if (index < endIndex && text[index] == '(') {
					int comment = index;

					if (!ParseUtils.SkipComment (text, ref index, endIndex)) {
						if (throwOnError)
							throw new ParseException (string.Format ("Incomplete comment token at offset {0}", comment), comment, index);

						return false;
					}

					comment++;

					name = Rfc2047.DecodePhrase (options, text, comment, (index - 1) - comment).Trim ();
				} else {
					name = string.Empty;
				}

				address = new MailboxAddress (name, addrspec);

				return true;
			}

			if (throwOnError)
				throw new ParseException (string.Format ("Invalid address token at offset {0}", startIndex), startIndex, index);

			return false;
		}

		/// <summary>
		/// Tries to parse the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns><c>true</c>, if the address was successfully parsed, <c>false</c> otherwise.</returns>
		/// <param name="options">The parser options to use.</param>
		/// <param name="buffer">The input buffer.</param>
		/// <param name="startIndex">The starting index of the input buffer.</param>
		/// <param name="length">The number of bytes in the input buffer to parse.</param>
		/// <param name="address">The parsed address.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="buffer"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="startIndex"/> and <paramref name="length"/> do not specify
		/// a valid range in the byte array.
		/// </exception>
		public static bool TryParse (ParserOptions options, byte[] buffer, int startIndex, int length, out InternetAddress address)
		{
			if (options == null)
				throw new ArgumentNullException ("options");

			if (buffer == null)
				throw new ArgumentNullException ("buffer");

			if (startIndex < 0 || startIndex >= buffer.Length)
				throw new ArgumentOutOfRangeException ("startIndex");

			if (length < 0 || startIndex + length >= buffer.Length)
				throw new ArgumentOutOfRangeException ("length");

			int index = startIndex;

			return TryParse (options, buffer, ref index, startIndex + length, false, out address);
		}

		/// <summary>
		/// Tries to parse the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns><c>true</c>, if the address was successfully parsed, <c>false</c> otherwise.</returns>
		/// <param name="buffer">The input buffer.</param>
		/// <param name="startIndex">The starting index of the input buffer.</param>
		/// <param name="length">The number of bytes in the input buffer to parse.</param>
		/// <param name="address">The parsed address.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="buffer"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="startIndex"/> and <paramref name="length"/> do not specify
		/// a valid range in the byte array.
		/// </exception>
		public static bool TryParse (byte[] buffer, int startIndex, int length, out InternetAddress address)
		{
			return TryParse (ParserOptions.Default, buffer, startIndex, length, out address);
		}

		/// <summary>
		/// Tries to parse the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns><c>true</c>, if the address was successfully parsed, <c>false</c> otherwise.</returns>
		/// <param name="options">The parser options to use.</param>
		/// <param name="buffer">The input buffer.</param>
		/// <param name="startIndex">The starting index of the input buffer.</param>
		/// <param name="address">The parsed address.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="buffer"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="startIndex"/> is out of range.
		/// </exception>
		public static bool TryParse (ParserOptions options, byte[] buffer, int startIndex, out InternetAddress address)
		{
			if (options == null)
				throw new ArgumentNullException ("options");

			if (buffer == null)
				throw new ArgumentNullException ("buffer");

			if (startIndex < 0 || startIndex >= buffer.Length)
				throw new ArgumentOutOfRangeException ("startIndex");

			int index = startIndex;

			return TryParse (options, buffer, ref index, buffer.Length, false, out address);
		}

		/// <summary>
		/// Tries to parse the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns><c>true</c>, if the address was successfully parsed, <c>false</c> otherwise.</returns>
		/// <param name="buffer">The input buffer.</param>
		/// <param name="startIndex">The starting index of the input buffer.</param>
		/// <param name="address">The parsed address.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="buffer"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="startIndex"/> is out of range.
		/// </exception>
		public static bool TryParse (byte[] buffer, int startIndex, out InternetAddress address)
		{
			return TryParse (ParserOptions.Default, buffer, startIndex, out address);
		}

		/// <summary>
		/// Tries to parse the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns><c>true</c>, if the address was successfully parsed, <c>false</c> otherwise.</returns>
		/// <param name="options">The parser options to use.</param>
		/// <param name="buffer">The input buffer.</param>
		/// <param name="address">The parsed address.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="buffer"/> is <c>null</c>.</para>
		/// </exception>
		public static bool TryParse (ParserOptions options, byte[] buffer, out InternetAddress address)
		{
			if (options == null)
				throw new ArgumentNullException ("options");

			if (buffer == null)
				throw new ArgumentNullException ("buffer");

			int index = 0;

			return TryParse (options, buffer, ref index, buffer.Length, false, out address);
		}

		/// <summary>
		/// Tries to parse the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns><c>true</c>, if the address was successfully parsed, <c>false</c> otherwise.</returns>
		/// <param name="buffer">The input buffer.</param>
		/// <param name="address">The parsed address.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="buffer"/> is <c>null</c>.
		/// </exception>
		public static bool TryParse (byte[] buffer, out InternetAddress address)
		{
			return TryParse (ParserOptions.Default, buffer, out address);
		}

		/// <summary>
		/// Tries to parse the given text into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the text and ignores the rest.
		/// </remarks>
		/// <returns><c>true</c>, if the address was successfully parsed, <c>false</c> otherwise.</returns>
		/// <param name="options">The parser options to use.</param>
		/// <param name="text">The text.</param>
		/// <param name="address">The parsed address.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="text"/> is <c>null</c>.
		/// </exception>
		public static bool TryParse (ParserOptions options, string text, out InternetAddress address)
		{
			if (options == null)
				throw new ArgumentNullException ("options");

			if (text == null)
				throw new ArgumentNullException ("text");

			var buffer = Encoding.UTF8.GetBytes (text);
			int index = 0;

			return TryParse (options, buffer, ref index, buffer.Length, false, out address);
		}

		/// <summary>
		/// Tries to parse the given text into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the text and ignores the rest.
		/// </remarks>
		/// <returns><c>true</c>, if the address was successfully parsed, <c>false</c> otherwise.</returns>
		/// <param name="text">The text.</param>
		/// <param name="address">The parsed address.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="text"/> is <c>null</c>.
		/// </exception>
		public static bool TryParse (string text, out InternetAddress address)
		{
			return TryParse (ParserOptions.Default, text, out address);
		}

		/// <summary>
		/// Parses the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns>The parsed <see cref="MimeKit.InternetAddress"/>.</returns>
		/// <param name="options">The parser options to use.</param>
		/// <param name="buffer">The input buffer.</param>
		/// <param name="startIndex">The starting index of the input buffer.</param>
		/// <param name="length">The number of bytes in the input buffer to parse.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="buffer"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="startIndex"/> and <paramref name="length"/> do not specify
		/// a valid range in the byte array.
		/// </exception>
		/// <exception cref="MimeKit.ParseException">
		/// <paramref name="buffer"/> could not be parsed.
		/// </exception>
		public static InternetAddress Parse (ParserOptions options, byte[] buffer, int startIndex, int length)
		{
			InternetAddress address;
			int index = startIndex;

			if (options == null)
				throw new ArgumentNullException ("options");

			if (buffer == null)
				throw new ArgumentNullException ("buffer");

			if (startIndex < 0 || startIndex > buffer.Length)
				throw new ArgumentOutOfRangeException ("startIndex");

			if (length < 0 || startIndex + length > buffer.Length)
				throw new ArgumentOutOfRangeException ("length");

			TryParse (options, buffer, ref index, startIndex + length, true, out address);

			return address;
		}

		/// <summary>
		/// Parses the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns>The parsed <see cref="MimeKit.InternetAddress"/>.</returns>
		/// <param name="buffer">The input buffer.</param>
		/// <param name="startIndex">The starting index of the input buffer.</param>
		/// <param name="length">The number of bytes in the input buffer to parse.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="buffer"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="startIndex"/> and <paramref name="length"/> do not specify
		/// a valid range in the byte array.
		/// </exception>
		/// <exception cref="MimeKit.ParseException">
		/// <paramref name="buffer"/> could not be parsed.
		/// </exception>
		public static InternetAddress Parse (byte[] buffer, int startIndex, int length)
		{
			return Parse (ParserOptions.Default, buffer, startIndex, length);
		}

		/// <summary>
		/// Parses the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns>The parsed <see cref="MimeKit.InternetAddress"/>.</returns>
		/// <param name="options">The parser options to use.</param>
		/// <param name="buffer">The input buffer.</param>
		/// <param name="startIndex">The starting index of the input buffer.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="buffer"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="startIndex"/>is out of range.
		/// </exception>
		/// <exception cref="MimeKit.ParseException">
		/// <paramref name="buffer"/> could not be parsed.
		/// </exception>
		public static InternetAddress Parse (ParserOptions options, byte[] buffer, int startIndex)
		{
			InternetAddress address;
			int index = startIndex;

			if (options == null)
				throw new ArgumentNullException ("options");

			if (buffer == null)
				throw new ArgumentNullException ("buffer");

			if (startIndex < 0 || startIndex > buffer.Length)
				throw new ArgumentOutOfRangeException ("startIndex");

			TryParse (options, buffer, ref index, buffer.Length, true, out address);

			return address;
		}

		/// <summary>
		/// Parses the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns>The parsed <see cref="MimeKit.InternetAddress"/>.</returns>
		/// <param name="buffer">The input buffer.</param>
		/// <param name="startIndex">The starting index of the input buffer.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="buffer"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="startIndex"/> is out of range.
		/// </exception>
		/// <exception cref="MimeKit.ParseException">
		/// <paramref name="buffer"/> could not be parsed.
		/// </exception>
		public static InternetAddress Parse (byte[] buffer, int startIndex)
		{
			return Parse (ParserOptions.Default, buffer, startIndex);
		}

		/// <summary>
		/// Parses the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns>The parsed <see cref="MimeKit.InternetAddress"/>.</returns>
		/// <param name="options">The parser options to use.</param>
		/// <param name="buffer">The input buffer.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="buffer"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="MimeKit.ParseException">
		/// <paramref name="buffer"/> could not be parsed.
		/// </exception>
		public static InternetAddress Parse (ParserOptions options, byte[] buffer)
		{
			InternetAddress address;
			int index = 0;

			if (options == null)
				throw new ArgumentNullException ("options");

			if (buffer == null)
				throw new ArgumentNullException ("buffer");

			TryParse (options, buffer, ref index, buffer.Length, true, out address);

			return address;
		}

		/// <summary>
		/// Parses the given input buffer into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the buffer and ignores the rest.
		/// </remarks>
		/// <returns>The parsed <see cref="MimeKit.InternetAddress"/>.</returns>
		/// <param name="buffer">The input buffer.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="buffer"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="MimeKit.ParseException">
		/// <paramref name="buffer"/> could not be parsed.
		/// </exception>
		public static InternetAddress Parse (byte[] buffer)
		{
			return Parse (ParserOptions.Default, buffer);
		}

		/// <summary>
		/// Parses the given text into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the text and ignores the rest.
		/// </remarks>
		/// <returns>The parsed <see cref="MimeKit.InternetAddress"/>.</returns>
		/// <param name="options">The parser options to use.</param>
		/// <param name="text">The text.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <c>null</c>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="text"/> is <c>null</c>.</para>
		/// </exception>
		/// <exception cref="MimeKit.ParseException">
		/// <paramref name="text"/> could not be parsed.
		/// </exception>
		public static InternetAddress Parse (ParserOptions options, string text)
		{
			if (options == null)
				throw new ArgumentNullException ("options");

			if (text == null)
				throw new ArgumentNullException ("text");

			var buffer = Encoding.UTF8.GetBytes (text);
			InternetAddress address;
			int index = 0;

			TryParse (options, buffer, ref index, buffer.Length, true, out address);

			return address;
		}

		/// <summary>
		/// Parses the given text into a new <see cref="MimeKit.InternetAddress"/> instance.
		/// </summary>
		/// <remarks>
		/// Parses only the first address in the text and ignores the rest.
		/// </remarks>
		/// <returns>The parsed <see cref="MimeKit.InternetAddress"/>.</returns>
		/// <param name="text">The text.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="text"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="MimeKit.ParseException">
		/// <paramref name="text"/> could not be parsed.
		/// </exception>
		public static InternetAddress Parse (string text)
		{
			return Parse (ParserOptions.Default, text);
		}
	}
}
